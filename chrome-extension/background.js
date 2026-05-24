const HOST_NAME = 'com.youtube_downloader';

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message.action === 'startDownload') {
    handleDownload(message.urls, message.path);
    sendResponse({ started: true });
  }
  return true;
});

function handleDownload(urls, path) {
  let port;

  try {
    port = chrome.runtime.connectNative(HOST_NAME);
  } catch (e) {
    chrome.runtime.sendMessage({
      action: 'downloadError',
      error: `ネイティブアプリへの接続に失敗しました: ${e.message}`
    });
    return;
  }

  port.onMessage.addListener((msg) => {
    switch (msg.type) {
      case 'progress':
        chrome.runtime.sendMessage({
          action: 'progress',
          current: msg.current,
          total: msg.total,
          title: msg.title ?? ''
        });
        break;
      case 'complete':
        chrome.runtime.sendMessage({
          action: 'complete',
          downloaded: msg.downloaded,
          failed: msg.failed
        });
        port.disconnect();
        break;
      case 'error':
        chrome.runtime.sendMessage({
          action: 'downloadError',
          error: msg.message
        });
        port.disconnect();
        break;
    }
  });

  port.onDisconnect.addListener(() => {
    const err = chrome.runtime.lastError;
    if (err) {
      chrome.runtime.sendMessage({
        action: 'downloadError',
        error: err.message
      });
    }
  });

  port.postMessage({ type: 'download', urls, path });
}
