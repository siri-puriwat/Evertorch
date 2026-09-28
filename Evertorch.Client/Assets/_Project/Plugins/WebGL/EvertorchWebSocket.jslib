// The browser's WebSocket for a web build (Network Protocol §7), polled from C# (BrowserWebSocketConnection). It
// only queues what arrives; the game connection's rules live in C#.
var EvertorchWebSocket = {
  $EvertorchSockets: { next: 1, table: {} },

  // States as WebSocketConnectionState: 0 connecting, 1 open, 2 closed, 3 failed (closed before it opened).
  EvertorchSocketOpen: function (urlPointer) {
    var id = EvertorchSockets.next++;
    var entry = { socket: null, state: 0, inbox: [] };
    EvertorchSockets.table[id] = entry;
    try {
      entry.socket = new WebSocket(UTF8ToString(urlPointer));
    } catch (error) {
      entry.state = 3;
      return id;
    }

    entry.socket.binaryType = 'arraybuffer';
    entry.socket.onopen = function () {
      entry.state = 1;
    };
    entry.socket.onmessage = function (event) {
      if (event.data instanceof ArrayBuffer) {
        entry.inbox.push(new Uint8Array(event.data));
      } else {
        entry.socket.close(1003);
      }
    };
    entry.socket.onclose = function () {
      entry.state = entry.state === 0 ? 3 : 2;
    };
    return id;
  },

  EvertorchSocketState: function (id) {
    var entry = EvertorchSockets.table[id];
    return entry ? entry.state : 3;
  },

  EvertorchSocketSend: function (id, pointer, length) {
    var entry = EvertorchSockets.table[id];
    if (!entry || entry.state !== 1) {
      return 0;
    }

    entry.socket.send(HEAPU8.slice(pointer, pointer + length));
    return 1;
  },

  EvertorchSocketReceive: function (id, pointer, capacity) {
    var entry = EvertorchSockets.table[id];
    if (!entry || entry.inbox.length === 0) {
      return -1;
    }

    var message = entry.inbox.shift();
    if (message.length > capacity) {
      return -2;
    }

    HEAPU8.set(message, pointer);
    return message.length;
  },

  EvertorchSocketClose: function (id) {
    var entry = EvertorchSockets.table[id];
    if (entry && entry.socket && entry.state < 2) {
      entry.socket.close(1000);
    }
  },

  EvertorchSocketFree: function (id) {
    var entry = EvertorchSockets.table[id];
    if (entry && entry.socket && entry.state < 2) {
      entry.socket.close(1000);
    }

    delete EvertorchSockets.table[id];
  }
};

autoAddDeps(EvertorchWebSocket, '$EvertorchSockets');
mergeInto(LibraryManager.library, EvertorchWebSocket);
