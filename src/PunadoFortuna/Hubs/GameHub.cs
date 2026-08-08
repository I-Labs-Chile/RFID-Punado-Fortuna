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
        var states = _gameEngine.GetAllZoneStates();
        await Clients.Caller.SendAsync("GameStateInit", states);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Cliente desconectado: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task ForceReset(int zonaId)
    {
        _gameEngine.ForceReset(zonaId);
    }

    public async Task ForceResetAll()
    {
        _gameEngine.ForceResetAll();
    }
}
