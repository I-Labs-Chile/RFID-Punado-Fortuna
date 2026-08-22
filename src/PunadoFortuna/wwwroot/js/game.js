(function () {
  'use strict';

  var phaseBadge = document.getElementById('phaseBadge');
  var tagCounterContainer = document.getElementById('tagCounterContainer');
  var tagCount = document.getElementById('tagCount');
  var colorGridContainer = document.getElementById('colorGridContainer');
  var colorGrid = document.getElementById('colorGrid');
  var enterHint = document.getElementById('enterHint');
  var gameArea = document.querySelector('.game-area');
  var connDot = document.getElementById('connDot');
  var connLabel = document.getElementById('connLabel');
  var reconnectBanner = document.getElementById('reconnectBanner');

  var connection = null;
  var connected = false;
  var currentPhase = 'WAITING';

  var COLORS = {
    rojo: '#ff4444',
    azul: '#4488ff',
    verde: '#44cc44',
    amarillo: '#ffdd00',
    naranja: '#ff8800',
    morado: '#9944ff',
    rosa: '#ff66aa',
    rosado: '#ff66aa',
    celeste: '#66ccff',
    blanco: '#eeeeee',
    negro: '#333333',
    gris: '#888888',
    marron: '#8B4513',
    turquesa: '#00ccbb',
    desconocido: '#666688',
    premio: '#ffd700'
  };

  function showBanner(text, cls) {
    reconnectBanner.textContent = text;
    reconnectBanner.className = 'reconnect-banner visible ' + (cls || '');
  }

  function hideBanner() {
    reconnectBanner.className = 'reconnect-banner';
  }

  function setConnectionStatus(status) {
    connDot.className = 'conn-dot';
    if (status === 'connected') {
      connDot.classList.add('connected');
      connLabel.textContent = 'CONECTADO';
      connLabel.style.color = '#22dd66';
      hideBanner();
      connected = true;
    } else if (status === 'reconnecting') {
      connDot.classList.add('reconnecting');
      connLabel.textContent = 'RECONECTANDO...';
      connLabel.style.color = '#ffd700';
      showBanner('RECONECTANDO...', 'reconnecting');
      connected = false;
    } else {
      connDot.classList.add('disconnected');
      connLabel.textContent = 'DESCONECTADO';
      connLabel.style.color = '#ff4444';
      connected = false;
    }
  }

  function getPhaseText(state) {
    switch (state.phase) {
      case 'WAITING': return state.isStable ? 'LISTO' : 'ESPERANDO PIEZAS';
      case 'REVEAL_COUNT': return 'HAY ' + state.tagCount + ' PIEZAS';
      case 'GUESS_COLORS': return '¿DE QUÉ COLORES SON?';
      case 'REVEAL_COLORS': return 'COLORES';
      default: return state.phase;
    }
  }

  function renderState(state) {
    if (!state) return;
    currentPhase = state.phase;

    // Badge
    phaseBadge.textContent = getPhaseText(state);
    phaseBadge.className = 'phase-badge';
    if (state.phase === 'REVEAL_COUNT' || state.phase === 'REVEAL_COLORS') {
      phaseBadge.classList.add('reveal');
    } else if (state.phase === 'GUESS_COLORS') {
      phaseBadge.classList.add('guess');
    }
    if (state.phase === 'WAITING' && state.isStable) {
      phaseBadge.classList.add('ready');
    }

    // Tag counter (visible in all phases except initial waiting-without-tags)
    if (state.phase !== 'WAITING') {
      tagCounterContainer.classList.remove('hidden');
      tagCount.textContent = state.tagCount;
    } else {
      tagCounterContainer.classList.add('hidden');
    }

    // Color grid
    if (state.phase === 'REVEAL_COLORS' && state.colorBreakdown && Object.keys(state.colorBreakdown).length > 0) {
      var html = '';
      var keys = Object.keys(state.colorBreakdown);
      for (var i = 0; i < keys.length; i++) {
        var color = keys[i];
        var count = state.colorBreakdown[color];
        var hex = COLORS[color] || COLORS.desconocido;
        html += '<div class="color-chip"><div class="chip-circle" style="background:' + hex + ';color:' + hex + '"></div><div class="chip-count">' + count + '</div></div>';
      }
      colorGrid.innerHTML = html;
      colorGridContainer.classList.remove('hidden');
    } else {
      colorGridContainer.classList.add('hidden');
      colorGrid.innerHTML = '';
    }

    // Enter hint
    if (state.phase === 'WAITING') {
      enterHint.classList.add('hidden');
    } else {
      enterHint.classList.remove('hidden');
    }

    // Game area glow
    if (state.phase === 'REVEAL_COUNT' || state.phase === 'REVEAL_COLORS') {
      gameArea.classList.add('revealed');
    } else {
      gameArea.classList.remove('revealed');
    }
  }

  function advancePhase() {
    if (connection && connected) {
      connection.invoke('AdvancePhase').catch(function (err) {
        console.error('AdvancePhase error:', err);
      });
    }
  }

  function reset() {
    if (connection && connected) {
      connection.invoke('Reset').catch(function (err) {
        console.error('Reset error:', err);
      });
    }
  }

  function initSignalR() {
    if (typeof signalR === 'undefined') {
      connLabel.textContent = 'SIGNALR NO DISPONIBLE';
      connLabel.style.color = '#ffd700';
      return;
    }

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/gamehub')
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on('GameStateUpdate', renderState);

    connection.on('ConnectionChanged', function (isConnected) {
      setConnectionStatus(isConnected ? 'connected' : 'disconnected');
    });

    connection.onreconnecting(function () {
      setConnectionStatus('reconnecting');
    });

    connection.onreconnected(function () {
      setConnectionStatus('connected');
    });

    connection.onclose(function () {
      setConnectionStatus('disconnected');
      setTimeout(function () {
        if (connection) connection.start().catch(function () {});
      }, 5000);
    });

    connection.start().then(function () {
      setConnectionStatus('connected');
    }).catch(function (err) {
      console.error('SignalR connection failed:', err);
      setConnectionStatus('disconnected');
      setTimeout(function () {
        if (connection) connection.start().catch(function () {});
      }, 5000);
    });
  }

  // Keyboard
  document.addEventListener('keydown', function (e) {
    if (e.key === 'F1') { e.preventDefault(); reset(); }
    if (e.key === 'Enter') { e.preventDefault(); advancePhase(); }
  });

  initSignalR();
})();
