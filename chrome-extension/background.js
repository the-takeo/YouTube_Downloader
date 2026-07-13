const HOST_NAME = 'com.youtube_downloader';
const KEEPALIVE_ALARM = 'dl-keepalive';

let port = null;

let state = {
  active: false,
  status: 'idle', // idle | running | complete | error | cancelled
  current: 0,
  total: 0,
  title: '',
  downloaded: 0,
  failed: 0,
  error: null
};

// アラームが発火すること自体が、ダウンロード中にサービスワーカーが
// アイドルタイムアウトで終了（＝ネイティブポートが切断されダウンロードが中断）
// するのを防ぐ。処理内容自体は不要。
chrome.alarms.onAlarm.addListener(() => {});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message.action === 'startDownload') {
    startDownload(message.urls, message.path);
    sendResponse({ started: true });
  } else if (message.action === 'cancelDownload') {
    cancelDownload();
    sendResponse({ cancelled: true });
  } else if (message.action === 'getStatus') {
    sendResponse(state);
  }
  return true;
});

function startDownload(urls, path) {
  if (state.active) return;

  state = {
    active: true,
    status: 'running',
    current: 0,
    total: urls.length,
    title: '',
    downloaded: 0,
    failed: 0,
    error: null
  };
  broadcast({ action: 'progress', current: 0, total: urls.length, title: '' });

  chrome.alarms.create(KEEPALIVE_ALARM, { periodInMinutes: 0.4 });

  try {
    port = chrome.runtime.connectNative(HOST_NAME);
  } catch (e) {
    finishWithError(`ネイティブアプリへの接続に失敗しました: ${e.message}`);
    return;
  }

  port.onMessage.addListener((msg) => {
    switch (msg.type) {
      case 'progress':
        state.current = msg.current;
        state.total = msg.total;
        state.title = msg.title ?? '';
        broadcast({ action: 'progress', current: msg.current, total: msg.total, title: state.title });
        break;
      case 'complete':
        state.active = false;
        state.status = 'complete';
        state.downloaded = msg.downloaded;
        state.failed = msg.failed;
        broadcast({ action: 'complete', downloaded: msg.downloaded, failed: msg.failed });
        cleanup();
        break;
      case 'cancelled':
        state.active = false;
        state.status = 'cancelled';
        state.downloaded = msg.downloaded;
        state.failed = msg.failed;
        broadcast({ action: 'cancelled', downloaded: msg.downloaded, failed: msg.failed });
        cleanup();
        break;
      case 'error':
        finishWithError(msg.message);
        break;
    }
  });

  port.onDisconnect.addListener(() => {
    if (!state.active) return;
    const err = chrome.runtime.lastError;
    finishWithError(err ? err.message : '接続が切断されました。');
  });

  port.postMessage({ type: 'download', urls, path });
}

function cancelDownload() {
  if (!state.active || !port) return;
  port.postMessage({ type: 'cancel' });
}

function finishWithError(message) {
  state.active = false;
  state.status = 'error';
  state.error = message;
  broadcast({ action: 'downloadError', error: message });
  cleanup();
}

function cleanup() {
  chrome.alarms.clear(KEEPALIVE_ALARM);
  if (port) {
    try { port.disconnect(); } catch { /* already gone */ }
    port = null;
  }
}

function broadcast(message) {
  chrome.runtime.sendMessage(message).catch(() => {
    // ポップアップが閉じていて受信側がいない場合は無視して構わない。
    // 状態自体はstateに保持されており、次にポップアップを開いたときに
    // getStatusで復元される。
  });
}
