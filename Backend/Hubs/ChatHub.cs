﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Ajax.Utilities;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using OnlineBanking.BLL.Repositories;
using OnlineBanking.DAL;
using static System.String;

namespace Backend.Hubs
{
    [HubName("chatHub")]
    public class ChatHub : Hub
    {
        #region Properties

        /// <summary>
        /// List of online users
        /// </summary>
        private static readonly List<UserViewModel> Connections = new List<UserViewModel>();

        /// <summary>
        /// Mapping SignalR connections to application users.
        /// (We don't want to share connectionId)
        /// </summary>
        private static readonly ConnectionMapping<string> ConnectionsMap = new ConnectionMapping<string>();

        #endregion

        private readonly IRepository<Accounts> accountRepo;
        private readonly IRepository<Channels> channelRepo;
        private readonly IRepository<Messages> messageRepo;
        private readonly IRepository<Notifications> notificationRepo;
        private readonly IRepository<Transactions> transactionRepo;

        public ChatHub()
        {
            accountRepo = new Repository<Accounts>();
            channelRepo = new Repository<Channels>();
            messageRepo = new Repository<Messages>();
            notificationRepo = new Repository<Notifications>();
            transactionRepo = new Repository<Transactions>();
        }

        public int SendPrivate(string message)
        {
            try
            {
                var account = FindAccountByAccountId(GetIntegerAccountId());
                var channel = FindChannelByAccountId(account.AccountId);

                // Create and save message in database
                var msg = new Messages
                {
                    AccountId = account.AccountId,
                    ChannelId = channel.ChannelId,
                    Content = Regex.Replace(message, @"(?i)<(?!img|a|/a|/img).*?>", Empty),
                };
                messageRepo.Add(msg);

                // Broadcast the message
                var messageViewModel = new MessageViewModel(msg, account.Name);
                Clients.Group(channel.ChannelId.ToString()).newMessage(messageViewModel);
                Clients.All.reloadChatData();

                return msg.MessageId;
            }
            catch (Exception)
            {
                Clients.Caller.onError("Message can't not send!");
            }

            return 0;
        }

        public int SendToChannel(int channelId, string message)
        {
            try
            {
                var account = FindAccountByAccountId(GetIntegerAccountId());
                var channel = FindChannelByChannelId(channelId);

                // Create and save message in database
                var msg = new Messages
                {
                    AccountId = account.AccountId,
                    ChannelId = channel.ChannelId,
                    Content = Regex.Replace(message, @"(?i)<(?!img|a|/a|/img).*?>", Empty),
                };
                messageRepo.Add(msg);

                // Broadcast the message
                var messageViewModel = new MessageViewModel(msg, account.Name);
                Clients.Group(channelId.ToString()).newMessage(messageViewModel);
                Clients.All.reloadChatData();

                return msg.MessageId;
            }
            catch (Exception)
            {
                Clients.Caller.onError("Message can't not send!");
            }

            return 0;
        }

        public IEnumerable<MessageViewModel> GetMessageHistory(int channelId, int messageId = 0)
        {
            if (Utils.IsNullOrEmpty(channelId) || channelId == 0) return null;
            IEnumerable<Messages> messageHistory;

            if (!Utils.IsNullOrEmpty(messageId) && messageId != 0)
            {
                messageHistory = messageRepo.Get()
                    .Where(m => m.Channel.ChannelId == channelId && m.MessageId < messageId)
                    .OrderByDescending(m => m.MessageId);
            }
            else
            {
                messageHistory = messageRepo.Get()
                    .Where(m => m.Channel.ChannelId == channelId)
                    .OrderByDescending(m => m.CreatedAt);
            }

            return messageHistory
                .Take(10)
                .AsEnumerable()
                .Reverse()
                .Select(x =>
                {
                    var account = FindAccountByAccountId(x.AccountId);

                    return new MessageViewModel(x, account?.Name);
                });
        }

        public IEnumerable<NotificationViewModel> GetNotificationsHistory()
        {
            var notifications = notificationRepo.Get()
                .Where(m => m.AccountId == GetIntegerAccountId())
                .OrderByDescending(m => m.CreatedAt);

            return notifications
                .Take(10)
                .AsEnumerable()
                .Reverse()
                .Select(x =>
                {
                    var pkObject = transactionRepo
                        .Get().FirstOrDefault(y => y.TransactionId == x.PkId);

                    return new NotificationViewModel(x, pkObject);
                });
        }

        protected int SendNotification(Notifications notification)
        {
            try
            {
                // Create and save notification in database
                notificationRepo.Add(notification);

                var pkObject = transactionRepo
                    .Get().FirstOrDefault(y => y.TransactionId == notification.PkId);

                // Broadcast the message
                var notificationViewModel = new NotificationViewModel(notification, pkObject);

                ConnectionsMap.GetConnections(notification.AccountId.ToString()).ForEach(connectionId =>
                {
                    Clients.Client(connectionId).historyNotifications(GetNotificationsHistory());
                });

                return notification.NotificationId;
            }
            catch (Exception)
            {
                Clients.Caller.onError("Notification can't not send!");
            }

            return 0;
        }

        //private void sendListOnline()
        //{
        //    Clients.All.onlineList();
        //}

        public IEnumerable<UserViewModel> GetOnlineAccounts()
        {
            return Connections;
        }

        #region OnConnected/OnReconnected/OnDisconnected

        public override Task OnConnected()
        {
            var connection = GetConnectionId();
            var accountId = GetIntegerAccountId();

            try
            {
                var account = accountRepo.Get().FirstOrDefault(u => u.AccountId == accountId);

                if (Utils.NotNullOrEmpty(account))
                {
                    var userViewModel = new UserViewModel(account, 0);

                    if (account.RoleId != (int) RoleStatus.Admin && account.RoleId != (int) RoleStatus.Support)
                    {
                        var channel = FindChannelByAccountId(accountId);
                        userViewModel.CurrentChannelId = channel.ChannelId;
                        Groups.Add(connection, channel.ChannelId.ToString());
                        Clients.Client(connection).historyMessages(GetMessageHistory(channel.ChannelId));
                        Clients.Client(connection).historyNotifications(GetNotificationsHistory());
                    }

                    var tempAccount = Connections.FirstOrDefault(u => u.AccountId == accountId);
                    Connections.Remove(tempAccount);

                    Connections.Add(userViewModel);
                    Clients.All.UpdateUser(userViewModel);

                    ConnectionsMap.Add(accountId.ToString(), connection);
                }
            }
            catch (Exception ex)
            {
                Clients.Caller.onError("OnConnected:" + ex.Message);
            }

            return base.OnConnected();
        }

        public override Task OnReconnected()
        {
            OnConnected();

            return base.OnReconnected();
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            var accountId = GetIntegerAccountId();

            try
            {
                var tempAccount = Connections.FirstOrDefault(u => u.AccountId == accountId);
                Connections.Remove(tempAccount);

                Connections.Add(tempAccount);
                Clients.All.UpdateUser(tempAccount);

                // Remove mapping
                if (tempAccount != null) ConnectionsMap.Remove(tempAccount.AccountId.ToString(), GetConnectionId());
            }
            catch (Exception ex)
            {
                Clients.Caller.onError("OnDisconnected: " + ex.Message);
            }

            return base.OnDisconnected(stopCalled);
        }

        #endregion

        public Task Join(int channelId)
        {
            try
            {
                var connection = GetConnectionId();
                var account = Connections.FirstOrDefault(u => u.AccountId == GetIntegerAccountId());

                if (Utils.NotNullOrEmpty(account))
                {
                    if (account.CurrentChannelId != channelId)
                    {
                        // Join to new chat room
                        Leave(account.CurrentChannelId);
                        Groups.Add(connection, channelId.ToString());
                        Connections.Remove(account);

                        account.CurrentChannelId = channelId;

                        Connections.Add(account);
                        Clients.All.UpdateUser(account);

                        Clients.Client(connection).historyMessages(GetMessageHistory(channelId));

                        return Groups.Add(connection, channelId.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                Clients.Caller.onError("You failed to join the channel!" + ex.Message);
            }

            return null;
        }

        private Task Leave(int channelId)
        {
            return Groups.Remove(GetConnectionId(), channelId.ToString());
        }

        private Channels FindChannelByChannelId(int channelId)
        {
            return channelRepo.Get(x => x.ChannelId == channelId).FirstOrDefault();
        }

        private Channels FindChannelByAccountId(int accountId)
        {
            var channel = channelRepo.Get(x => x.UserId == accountId).FirstOrDefault();

            if (!Utils.IsNullOrEmpty(channel)) return channel;
            channel = new Channels(accountId);
            channelRepo.Add(channel);

            return channel;
        }

        private Accounts FindAccountByAccountId(int accountId)
        {
            return accountRepo.Get(x => x.AccountId == accountId).FirstOrDefault();
        }

        private int GetIntegerAccountId()
        {
            return int.Parse(GetAccountId());
        }

        private string GetAccountId()
        {
            return Context.QueryString["userId"];
        }

        private string GetConnectionId()
        {
            return Context.ConnectionId;
        }
    }
}