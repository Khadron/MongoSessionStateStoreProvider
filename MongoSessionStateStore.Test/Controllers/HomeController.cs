using System;
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
            //Session["test"] = "hehe";
            Session["khadron2"] = "k";
            return View();
        }

        public ActionResult Print()
        {
            //ViewBag.Test = Session["test"];
            //ViewBag.Test = Session["hehe"];
            ViewBag.Test = Session["khadron2"];
            Session.Abandon();

            return View();
        }
    }
}