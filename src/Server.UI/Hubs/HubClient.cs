﻿using System.Net;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace CleanArchitecture.Blazor.Server.UI.Hubs;

public sealed class HubClient : IAsyncDisposable
{
    public delegate Task MessageReceivedEventHandler(object sender, MessageReceivedEventArgs e);

    private readonly HubConnection _hubConnection;
    private bool _started;

    public HubClient(NavigationManager navigationManager, IHttpContextAccessor httpContextAccessor)
    {
        var uri = new UriBuilder(navigationManager.Uri);
        var container = new CookieContainer();
        if (httpContextAccessor.HttpContext != null)
            foreach (var c in httpContextAccessor.HttpContext.Request.Cookies)
                container.Add(new Cookie(c.Key, c.Value)
                {
                    Domain = uri.Host, Path // Set the domain of the cookie
                        = "/" // Set the path of the cookie
                });
        var hubUrl = navigationManager.BaseUri.TrimEnd('/') + ISignalRHub.Url;
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.Cookies = container;
            }).WithAutomaticReconnect().Build();

        _hubConnection.ServerTimeout = TimeSpan.FromSeconds(30);

        _hubConnection.On<string, string>(nameof(ISignalRHub.Connect),
            (connectionId, userName) => LoginEvent?.Invoke(this, new UserStateChangeEventArgs(connectionId, userName)));

        _hubConnection.On<string, string>(nameof(ISignalRHub.Disconnect),
            (connectionId, userName) =>
                LogoutEvent?.Invoke(this, new UserStateChangeEventArgs(connectionId, userName)));

        _hubConnection.On<string>(nameof(ISignalRHub.Start),
            message => JobStartedEvent?.Invoke(this, message));

        _hubConnection.On<string>(nameof(ISignalRHub.Completed),
            message => JobCompletedEvent?.Invoke(this, message));

        _hubConnection.On<string>(nameof(ISignalRHub.SendNotification),
            message => NotificationReceivedEvent?.Invoke(this, message));

        _hubConnection.On<string, string>(nameof(ISignalRHub.SendMessage),
            (from, message) => { MessageReceivedEvent?.Invoke(this, new MessageReceivedEventArgs(from, message)); });

        _hubConnection.On<string, string, string>(nameof(ISignalRHub.SendPrivateMessage),
            (from, to, message) =>
            {
                MessageReceivedEvent?.Invoke(this, new MessageReceivedEventArgs(from, message));
            });
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _hubConnection.StopAsync();
        }
        finally
        {
            await _hubConnection.DisposeAsync();
        }
    }

    public event EventHandler<UserStateChangeEventArgs>? LoginEvent;
    public event EventHandler<UserStateChangeEventArgs>? LogoutEvent;
    public event EventHandler<string>? JobStartedEvent;
    public event EventHandler<string>? JobCompletedEvent;
    public event EventHandler<string>? NotificationReceivedEvent;
    public event MessageReceivedEventHandler? MessageReceivedEvent;

    public async Task StartAsync(CancellationToken cancellation = default)
    {
        if (_started) return;
        _started = true;
        await _hubConnection.StartAsync(cancellation);
    }

    public async Task SendAsync(string message)
    {
        await _hubConnection.SendAsync(nameof(ISignalRHub.SendMessage), message);
    }

    public async Task NotifyAsync(string message)
    {
        await _hubConnection.SendAsync(nameof(ISignalRHub.SendNotification), message);
    }
}

public class MessageReceivedEventArgs : EventArgs
{
    public MessageReceivedEventArgs(string userName, string message)
    {
        UserId = userName;
        Message = message;
    }

    public string UserId { get; set; }
    public string Message { get; set; }
}

public class UserStateChangeEventArgs : EventArgs
{
    public UserStateChangeEventArgs(string connectionId, string userName)
    {
        ConnectionId = connectionId;
        UserName = userName;
    }

    public string ConnectionId { get; set; }
    public string UserName { get; set; }
}