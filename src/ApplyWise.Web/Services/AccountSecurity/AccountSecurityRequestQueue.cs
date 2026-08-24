using System.Threading.Channels;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.AccountSecurity;

public interface IAccountSecurityRequestQueue
{
    bool TryQueue(string email, AccountSecurityAction action);
}

public sealed class AccountSecurityRequestQueue(
    IServiceScopeFactory scopeFactory,
    ILogger<AccountSecurityRequestQueue> logger)
    : BackgroundService, IAccountSecurityRequestQueue
{
    private const int Capacity = 256;
    private readonly Channel<AccountSecurityRequest> _confirmationRequests = CreateQueue();
    private readonly Channel<AccountSecurityRequest> _recoveryRequests = CreateQueue();

    private static Channel<AccountSecurityRequest> CreateQueue() =>
        Channel.CreateBounded<AccountSecurityRequest>(new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    public bool TryQueue(string email, AccountSecurityAction action)
    {
        if (action is not (AccountSecurityAction.ConfirmEmail or AccountSecurityAction.ResetPassword))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        var queue = action == AccountSecurityAction.ResetPassword
            ? _recoveryRequests
            : _confirmationRequests;
        var accepted = queue.Writer.TryWrite(
            new AccountSecurityRequest(email.Trim(), action));
        if (!accepted)
        {
            logger.LogWarning(
                "The {Action} delivery queue is full; the caller will receive a retryable response.",
                action);
        }
        return accepted;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(
            ProcessAsync(_confirmationRequests.Reader, stoppingToken),
            ProcessAsync(_recoveryRequests.Reader, stoppingToken));

    private async Task ProcessAsync(
        ChannelReader<AccountSecurityRequest> reader,
        CancellationToken stoppingToken)
    {
        await foreach (var request in reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var securityCodes = scope.ServiceProvider.GetRequiredService<IAccountSecurityCodeService>();
                var adminOptions = scope.ServiceProvider
                    .GetRequiredService<IOptions<AdminAccessOptions>>()
                    .Value;
                if (request.Action == AccountSecurityAction.ConfirmEmail
                    && adminOptions.Contains(request.Email))
                {
                    continue;
                }

                var user = await userManager.FindByEmailAsync(request.Email);
                if (user is null)
                {
                    continue;
                }

                var isConfirmed = await userManager.IsEmailConfirmedAsync(user);
                var isEligible = request.Action switch
                {
                    AccountSecurityAction.ConfirmEmail => !isConfirmed,
                    AccountSecurityAction.ResetPassword => isConfirmed,
                    _ => false
                };
                if (!isEligible)
                {
                    continue;
                }

                await securityCodes.IssueAsync(
                    user.Id,
                    request.Email,
                    request.Action,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "An anonymous account-security delivery request could not be completed.");
            }
        }
    }

    private sealed record AccountSecurityRequest(string Email, AccountSecurityAction Action);
}
