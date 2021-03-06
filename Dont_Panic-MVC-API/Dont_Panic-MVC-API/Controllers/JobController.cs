﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using Dont_Panic_MVC_API.Models;
using System.Net.Http.Headers;
using Dont_Panic_MVC_API.Models.API_Models;
using Newtonsoft.Json;
using Dont_Panic_MVC_API.Controllers.API_Controllers;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

using System.Configuration;
using System.IO;
using Dont_Panic_MVC_API.API_Models;
using System.Web.Security;

using System.Net.Mail;
using SendGridMail;

namespace Dont_Panic_MVC_API.Controllers


{
   
    public class JobController : Controller
    {

        private JobAPI jobAPI = new JobAPI();

        public ActionResult Browse()
        {
            return View(jobAPI.GetAllJobs().ToList());
        }

        [Authorize]
        public ActionResult Previous()
        {
            return View(jobAPI.GetExpiredUserJobs(User.Identity.GetUserId()).ToList());
        }

        // GET: /Job/
        [Authorize]
        public ActionResult Index()
        {
            IdentityManager im = new IdentityManager();

            if (im.UserContainsRole(User.Identity.GetUserId(),"Provider"))
            {
                JobServiceAPI api = new JobServiceAPI();
                IQueryable<JobService> jobServices = api.getProvidersJobs(User.Identity.GetUserId());
                    
                List<Job> jobs = new List<Job>();
                APIContext context = new APIContext();
                foreach(var jobService in jobServices){
                    jobs.Add(context.Jobs.First(i => i.jobid == jobService.jobid));
                }
                return View(jobs);            
            }
            return View(jobAPI.GetCurrentUserJobs(User.Identity.GetUserId()).ToList());
        }

        // POST: /Job/RepostJob
        [HttpPost]
        public ActionResult RepostJob(int jobid)
        {
            JobAPI api = new JobAPI();
            
            Job job = api.GetJob(jobid);
            if (job != null)
            {
                job.expireDate = DateTime.Now.AddDays(2);
                api.PutJob(jobid, job);
            }
            return RedirectToAction("/");
        }

        // GET: /Job/InterestedPros
        [HttpGet]
        [Authorize]
        public ActionResult InterestedPros(int jobid)
        {

            JobServiceAPI api = new JobServiceAPI();
            IQueryable<JobService> jobproviders = api.getJobsProviders(jobid);

            ServiceAPI sapi = new ServiceAPI();
            APIContext context = new APIContext();

            List<UsersProviderDetails> providers = new List<UsersProviderDetails>();
            foreach (JobService js in jobproviders)
            {
                ServiceProviderDetails detail = context.ServiceProvidersDetails.First(s => s.userId == js.serviceProviderId);
                UsersProviderDetails udetail = new UsersProviderDetails();
                udetail.about = detail.about;
                udetail.address = detail.address;
                udetail.areas_serviced = detail.areas_serviced;
                udetail.availability = detail.availability;
                udetail.business_name = detail.business_name;
                udetail.contact_name = detail.contact_name;
                udetail.contact_number_1 = detail.contact_number_1;
                udetail.contact_number_2 = detail.contact_number_2;
                udetail.description = detail.description;
                udetail.userId = detail.userId;
                udetail.website_address = detail.website_address;
                udetail.username = detail.userName;
                udetail.email = detail.email;
                providers.Add(udetail);
            }
            return View(providers);
        }

        // GET: /Job/Details/5
        public ActionResult Details(int id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Job job = jobAPI.GetJob(id);

            if (job == null)
            {
                return HttpNotFound();
            }
            return View(job);
        }

        // POST: /Job/AcquireLead
        public ActionResult AcquireLead(int jobid)
        {
            APIContext db = new APIContext(); 
            IQueryable<JobService> jobServices = db.jobService.Where(a => a.jobid == jobid);
            
            // If at least one lead has been acquired
            if (jobServices != null) { 
                // Searching if leads have been acquired by this service provider.
                foreach (JobService jobService in jobServices)
                {   // If the service provider already owns a lead for this job then redirect away.
                    if (jobService.serviceProviderId == User.Identity.GetUserId())
                    {
                        return RedirectToAction("/");
                    }
                }
            }

            Job job = jobAPI.GetJob(jobid);
            if (job.leadsAccquired < 3)
            {

                ServiceAPI sapi = new ServiceAPI();
                sapi.removeTokens(1, User.Identity.GetUserId());


                job.leadsAccquired = job.leadsAccquired + 1;
                jobAPI.PutJob(jobid, job);

                JobService servicejob = new JobService();
                servicejob.jobid = jobid;
               
                servicejob.serviceProviderId = User.Identity.GetUserId();
                db.jobService.Add(servicejob);
                db.SaveChanges();
            }
            
            return RedirectToAction("/");
        }

        // GET: /Job/Create
        public ActionResult Create()
        {
            ViewBag.City = (new RegionDropDown()).RegionList;
            return View();
        }

        // POST: /Job/Create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(ViewJob viewJob)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Account",
                           new { returnUrl = "/Job/Create", FormMethod.Get});
            }
            
            Job jobmodel = new Job();

            if (ModelState.IsValid)
            {
               // Console.WriteLine(fakejobmodel.region.Text.ToString());

                APIContext context = new APIContext();

                jobmodel.region = context.region.First(r => r.regionid == viewJob.region).region;


                jobmodel.district =  context.district.First(d => d.districtid == viewJob.district).district;
                jobmodel.suburb = context.suburb.First(d => d.suburbid == viewJob.suburb).suburb; ;
                jobmodel.description = viewJob.description;
                jobmodel.jobtype = viewJob.jobtype;
                jobmodel.title = viewJob.title;
                
                jobmodel.submitDate = DateTime.Now;
                
                switch (viewJob.duration)
                {
                    case "Now (24 Hrs)":
                        jobmodel.expireDate = jobmodel.submitDate.AddDays(1);
                        break;
                    case "Soon (48 Hrs)":
                        jobmodel.expireDate = jobmodel.submitDate.AddDays(2);
                        break;
                    case "Whenever (72 Hrs +)":
                        jobmodel.expireDate = jobmodel.submitDate.AddDays(3);
                        break;
                    default:
                        jobmodel.expireDate = jobmodel.submitDate.AddDays(1);
                        break; 
                }

                jobmodel.UserId = User.Identity.GetUserId();
                jobmodel.username = User.Identity.GetUserName();
                jobAPI.PostJob(jobmodel);

                List<string> images = ImageUpload();
                foreach(string image in images){
                    Photos photo = new Photos();
                    photo.jobid = jobmodel.jobid;
                    photo.photo = image;
                    context.photos.Add(photo);
                }
                context.SaveChanges();
                
                List<ServiceProviderDetails> providers = context.ServiceProvidersDetails.ToList();

                foreach (ServiceProviderDetails detail in providers)
                {
                    if (detail.userId != null)
                    {
                       

                        // Create the email object first, then add the properties.
                        SendGrid myMessage = SendGrid.GetInstance();
                        myMessage.AddTo(detail.email);
                        myMessage.From = new MailAddress("no.reply-notifications@GoodJob.co.nz", "GoodJob.co.nz");
                        myMessage.Subject = "New job - " + jobmodel.title;
                        myMessage.Text = "Job Description\n" + jobmodel.description + "\n" + "www.GoodJob.co.nz/Job/Details/" + jobmodel.jobid;

                        // Create credentials, specifying your user name and password.
                        var credentials = new NetworkCredential("azure_1e527e7b7ec0d7c5eb3d21f6d1643c1e@azure.com", "9sixwieb");

                        // Create a REST transport for sending email.
                        var transportREST = Web.GetInstance(credentials);

                        // Send the email.
                        transportREST.Deliver(myMessage);
                    }
                }

               


                return RedirectToAction("Index");
            }
            ViewBag.City = (new RegionDropDown()).RegionList;

            return View(viewJob);
        }

        public List<string> ImageUpload()
        {

            HttpPostedFileBase file = Request.Files["images"];

            List<string> photos = new List<string>();

            for (int i = 0; i < Request.Files.Count; i++)
            {
                HttpPostedFileBase image = Request.Files[i];

                if (file.ContentLength == 0)
                    continue;

                ViewBag.UploadMessage = String.Format("Got image {0} of type {1} and size {2}",
                   image.FileName, image.ContentType, image.ContentLength);
                // TODO: actually save the image to Azure blob storage

                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                    ConfigurationManager.ConnectionStrings["StorageConnectionString"].ConnectionString);

                var blobStorage = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer container = blobStorage.GetContainerReference("job-images");
                if (container.CreateIfNotExist())
                {
                    // configure container for public access
                    var permissions = container.GetPermissions();
                    permissions.PublicAccess = BlobContainerPublicAccessType.Container;
                    container.SetPermissions(permissions);
                }
                string uniqueBlobName = string.Format("job-images/image_{0}{1}",
                Guid.NewGuid().ToString(), Path.GetExtension(image.FileName));
                CloudBlockBlob blob = blobStorage.GetBlockBlobReference(uniqueBlobName);

                blob.Properties.ContentType = image.ContentType;
                blob.UploadFromStream(image.InputStream);

                photos.Add(blob.Uri.ToString());
            }
            return photos;
        }


        [Authorize]
        public string AuthCreate(string title){


          
         //   if (ModelState.IsValid)
         //   {
        //        jobmodel.submitDate = DateTime.Today;
         //       jobmodel.expireDate = DateTime.Today.AddDays(2);
         //       jobmodel.UserId = User.Identity.GetUserId();
         //       jobmodel.username = User.Identity.GetUserName();
        //        jobAPI.PostJob(jobmodel);
      //          return ("Is valid");               // return RedirectToAction("Index");
        //    }
            return title;
          //  return RedirectToAction("Create", jobmodel);
        }

        // GET: /Job/Edit/5
         [Authorize]
        public ActionResult Edit(int id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            Job jobmodel = jobAPI.GetJob(id);
            if (!User.Identity.GetUserId().Equals(jobmodel.UserId))
            {
                return RedirectToAction("Error");
            }

            if (jobmodel == null)
            {
                return HttpNotFound();
            }
            return View(jobmodel);
        }

        // POST: /Job/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.

         [Authorize]
         [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include="jobid,title,description,city,jobtype")] int id, Job jobmodel)
        {
            Job job = jobAPI.GetJob(id);
            if (!User.Identity.GetUserId().Equals(job.UserId))
            {
                return RedirectToAction("Error");
            }
            jobmodel.UserId = User.Identity.GetUserId();

            APIContext db = new APIContext();
            List<string> images = ImageUpload();
            foreach (string image in images)
            {
                Photos photo = new Photos();
                photo.jobid = jobmodel.jobid;
                photo.photo = image;
                db.photos.Add(photo);
            }
            db.SaveChanges(); 

            if (ModelState.IsValid)
            {
                jobAPI.PutJob(id, jobmodel);
                return RedirectToAction("Index");
            }
            return View(jobmodel);
        }

        // GET: /Job/Delete/5

         [Authorize]
         [HttpGet]
        public ActionResult Delete(int id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            Job jobmodel = jobAPI.GetJob(id);

            if (!User.Identity.GetUserId().Equals(jobmodel.UserId))
            {
                return RedirectToAction("Error");
            }

            if (jobmodel == null)
            {
                return HttpNotFound();
            }
            return View(jobmodel);
        }

        // POST: /Job/Delete/5
         [Authorize]
         [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("Delete")]
        public ActionResult DeleteConfirmed(int id)
        {
            Job jobmodel = jobAPI.GetJob(id);
            // Checking for matching UserID so only the user can delete their listing. 
            if (!(jobmodel.UserId.Equals(User.Identity.GetUserId())))
            {
                RedirectToAction("Error");
            }
            jobAPI.DeleteJob(jobmodel);

            JobServiceAPI api = new JobServiceAPI();
            api.deleteJobService(id, User.Identity.GetUserId());

            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                jobAPI.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
