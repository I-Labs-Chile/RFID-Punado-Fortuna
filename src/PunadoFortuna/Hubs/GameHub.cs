using Microsoft.AspNetCore.SignalR;
using PunadoFortuna.Services;

namespace PunadoFortuna.Hubs;

public class GameHub : Hub
{
    private readonly GameEngine _gameEngine;
    private readonly ILogger<GameHub> _logger;

    public GameHub(GameEngine gameEngine, ILogger<GameHub> logger)
    {
        _gameEngine = gameEngine;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Cliente conectado: {ConnectionId}", Context.ConnectionId);
        await Clients.Caller.SendAsync("GameStateUpdate", _gameEngine.GetState());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Cliente desconectado: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task AdvancePhase()
    {
        _gameEngine.AdvancePhase();
    }

    public async Task Reset()
    {
        _gameEngine.Reset();
    }
}
