namespace ApplyWise.Web.Services.Security;

public sealed class ResourceLockUnavailableException(string message)
    : Exception(message);
