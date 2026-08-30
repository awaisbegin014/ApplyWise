using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ApplyWise.Web.Models;
using ApplyWise.Web.ViewModels.Home;

namespace ApplyWise.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        return View();
    }

    [HttpGet("/product/job-tracker")]
    public IActionResult JobTracker()
    {
        return View("Product", MarketingProductPages.JobTracker);
    }

    [HttpGet("/product/resume-builder")]
    public IActionResult ResumeBuilder()
    {
        return View("Product", MarketingProductPages.ResumeBuilder);
    }

    [HttpGet("/product/how-it-works")]
    public IActionResult HowItWorks()
    {
        return View("Product", MarketingProductPages.HowItWorks);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult Terms()
    {
        return View();
    }

    public IActionResult Support()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [ActionName("StatusCode")]
    public IActionResult HandleStatusCode(int code)
    {
        Response.StatusCode = code;
        var acceptsHtml = Request.Headers.Accept.Count == 0
            || Request.Headers.Accept.Any(value =>
                value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
        if (!acceptsHtml)
        {
            return Problem(
                statusCode: code,
                title: code == StatusCodes.Status404NotFound
                    ? "Resource not found"
                    : "Request could not be completed");
        }

        return View(new StatusCodeViewModel
        {
            StatusCode = code,
            Title = code == StatusCodes.Status404NotFound
                ? "We couldn’t find that page."
                : "We couldn’t complete that request.",
            Message = code == StatusCodes.Status404NotFound
                ? "The link may be outdated, or the item may no longer be available."
                : "Return to ApplyWise and try the action again."
        });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
