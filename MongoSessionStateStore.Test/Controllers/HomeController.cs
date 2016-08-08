﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace MongoSessionStateStore.Test.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            Session["test"] = "hehe";
            return View();
        }

        public ActionResult Print()
        {
            ViewBag.Test = Session["test"];
            return View();
        }
    }
}