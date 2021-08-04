﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;
using Backend.Areas.Admin.Data;
using OnlineBanking.BLL.Repositories;
using OnlineBanking.DAL;

namespace Backend.Areas.Admin.Controllers
{
    public class CurrenciesController : Controller
    {
        private ApplicationDbContext db = new ApplicationDbContext();
        private IRepository<Currencies> currencies;
        public CurrenciesController()
        {
            currencies = new Repository<Currencies>();
        }

        // GET: Admin/Currencies
        public ActionResult Index()
        {
            if (Session["email"] != null)
            {
                ViewBag.Currency = "active";
                return View();
            }
            else
            {
                return RedirectToAction("Login", "Home", new { area = "" });
            }
        }

        public ActionResult GetData(int page = 1, string key = null, int pageSize = 5)
        {
            ViewBag.Accounts = "active";

            var data = currencies.Get();

            if (!string.IsNullOrEmpty(key))
            {
                data = data.Where(x => x.Name.Contains(key));
            }
            decimal totalpage = Math.Ceiling((decimal)data.Count() / pageSize);

            return Json(new
            {
                totalPages = totalpage,
                currentPage = page,
                data = data.Skip((page - 1) * pageSize).Take(pageSize)
            }, JsonRequestBehavior.AllowGet);
        }

        public ActionResult FindId(int id)
        {
            return Json(currencies.Get(id), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult PostData(Currencies c)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();

            if (ModelState.IsValid)
            {
                var check = currencies.Get(x => x.Name == c.Name);

                if (Utils.IsNullOrEmpty(check))
                {
                    var first = check.FirstOrDefault();

                    if (first.Status == (int)DefaultStatus.Deleted)
                    {
                        c.CurrencyId = first.CurrencyId;
                        c.Status = (int)DefaultStatus.Actived;
                        currencies.Update(c);

                        return Json(new
                        {
                            statusCode = 200,
                            message = "Success",
                            data = c
                        }, JsonRequestBehavior.AllowGet);
                    }
                    else
                    {
                        errors.Add("Name", "Name is dublicated!");

                        return Json(new
                        {
                            statusCode = 400,
                            message = "Error",
                            data = errors
                        }, JsonRequestBehavior.AllowGet);
                    }
                }

                c.Status = (int)DefaultStatus.Actived;
                currencies.Add(c);

                return Json(new
                {
                    statusCode = 200,
                    message = "Success",
                    data = c
                }, JsonRequestBehavior.AllowGet);
            }

            foreach (var k in ModelState.Keys)
                foreach (var err in ModelState[k].Errors)
                {
                    var key = Regex.Replace(k, @"(\w+)\.(\w+)", @"$2");
                    if (!errors.ContainsKey(key))
                        errors.Add(key, err.ErrorMessage);
                }

            return Json(new
            {
                statusCode = 400,
                message = "Error",
                data = errors
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult PutData(Currencies c)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();

            if (ModelState.IsValid)
            {
                var check = currencies.Get(x => x.CurrencyId != c.CurrencyId && x.Name == c.Name);

                if (Utils.IsNullOrEmpty(check))
                {
                    errors.Add("Name", "Name is dublicated!");

                    return Json(new
                    {
                        statusCode = 400,
                        message = "Error",
                        data = errors
                    }, JsonRequestBehavior.AllowGet);
                }

                c.Status = (int)DefaultStatus.Actived;
                currencies.Edit(c);

                return Json(new
                {
                    statusCode = 200,
                    message = "Success",
                    data = c
                }, JsonRequestBehavior.AllowGet);
            }

            foreach (var k in ModelState.Keys)
                foreach (var err in ModelState[k].Errors)
                {
                    var key = Regex.Replace(k, @"(\w+)\.(\w+)", @"$2");
                    if (!errors.ContainsKey(key))
                        errors.Add(key, err.ErrorMessage);
                }

            return Json(new
            {
                statusCode = 400,
                message = "Error",
                data = errors
            }, JsonRequestBehavior.AllowGet);
        }



        // GET: Admin/Currencies/Delete/5
        public ActionResult Delete(int id)
        {
            if (currencies.Delete(id))
            {
                return Json(new
                {
                    statusCode = 200,
                    message = "Success"
                }, JsonRequestBehavior.AllowGet);
            }

            return Json(new
            {
                statusCode = 400,
                message = "Error"
            }, JsonRequestBehavior.AllowGet);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool existCurrencyId(int id)
        {
            return Utils.IsNullOrEmpty(currencies.Get(id));
        }
    }
}