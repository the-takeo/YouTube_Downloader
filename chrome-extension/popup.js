const API_BASE = 'https://www.googleapis.com/youtube/v3';

let pageInfo = null;

document.addEventListener('DOMContentLoaded', async () => {
  const stored = await chrome.storage.sync.get(['apiKey', 'downloadPath']);
  if (stored.apiKey) document.getElementById('api-key').value = stored.apiKey;
  if (stored.downloadPath) document.getElementById('path').value = stored.downloadPath;

  document.getElementById('save-settings-btn').addEventListener('click', saveSettings);
  document.getElementById('download-btn').addEventListener('click', onDownloadClick);

  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  let url;
  try { url = new URL(tab.url); } catch { showNotSupported(); return; }

  const parsed = parseYouTubeUrl(url);
  if (!parsed) { showNotSupported(); return; }

  document.getElementById('main-form').style.display = 'block';

  const labels = { video: '動画', playlist: '再生リスト', channel: 'チャンネル', channel_handle: 'チャンネル', channel_name: 'チャンネル' };
  document.getElementById('page-type-label').textContent = labels[parsed.type] ?? 'チャンネル';

  if (parsed.type === 'video') {
    document.getElementById('count-field').style.display = 'none';
  }

  await resolvePageInfo(parsed, stored.apiKey);
});

function parseYouTubeUrl(url) {
  if (url.hostname !== 'www.youtube.com') return null;

  const videoId = url.searchParams.get('v');
  if (url.pathname === '/watch' && videoId) {
    return { type: 'video', id: videoId };
  }

  const listId = url.searchParams.get('list');
  if (url.pathname === '/playlist' && listId) {
    return { type: 'playlist', id: listId };
  }

  const channelMatch = url.pathname.match(/^\/channel\/(UC[\w-]+)/);
  if (channelMatch) return { type: 'channel', id: channelMatch[1] };

  const handleMatch = url.pathname.match(/^\/@([\w.-]+)/);
  if (handleMatch) return { type: 'channel_handle', handle: '@' + handleMatch[1] };

  const customMatch = url.pathname.match(/^\/c\/([\w.-]+)/);
  if (customMatch) return { type: 'channel_name', name: customMatch[1] };

  return null;
}

async function resolvePageInfo(parsed, apiKey) {
  if (!apiKey) {
    document.getElementById('page-name').textContent = parsed.id || parsed.handle || parsed.name || '（APIキーを設定してください）';
    if (parsed.type === 'channel_handle' || parsed.type === 'channel_name') {
      setStatus('チャンネルIDの解決にAPIキーが必要です。設定から入力してください。', true);
      return;
    }
    pageInfo = { type: parsed.type, id: parsed.id };
    document.getElementById('download-btn').disabled = false;
    return;
  }

  try {
    if (parsed.type === 'video') {
      const data = await apiGet(`videos?part=snippet&id=${parsed.id}&key=${apiKey}`);
      pageInfo = {
        type: 'video',
        id: parsed.id,
        name: data.items?.[0]?.snippet?.title || parsed.id
      };
    } else if (parsed.type === 'channel_handle') {
      const data = await apiGet(`channels?part=snippet&forHandle=${parsed.handle}&key=${apiKey}`);
      if (!data.items?.length) throw new Error('チャンネルが見つかりません');
      pageInfo = { type: 'channel', id: data.items[0].id, name: data.items[0].snippet.title };
    } else if (parsed.type === 'channel_name') {
      const data = await apiGet(`channels?part=snippet&forUsername=${parsed.name}&key=${apiKey}`);
      if (!data.items?.length) throw new Error('チャンネルが見つかりません');
      pageInfo = { type: 'channel', id: data.items[0].id, name: data.items[0].snippet.title };
    } else if (parsed.type === 'channel') {
      const data = await apiGet(`channels?part=snippet&id=${parsed.id}&key=${apiKey}`);
      pageInfo = {
        type: 'channel',
        id: parsed.id,
        name: data.items?.[0]?.snippet?.title || parsed.id
      };
    } else {
      const data = await apiGet(`playlists?part=snippet&id=${parsed.id}&key=${apiKey}`);
      pageInfo = {
        type: 'playlist',
        id: parsed.id,
        name: data.items?.[0]?.snippet?.title || parsed.id
      };
    }

    document.getElementById('page-name').textContent = pageInfo.name || pageInfo.id;
    document.getElementById('download-btn').disabled = false;
  } catch (e) {
    document.getElementById('page-name').textContent = parsed.id || '（取得失敗）';
    setStatus(`エラー: ${e.message}`, true);
  }
}

async function apiGet(path) {
  const res = await fetch(`${API_BASE}/${path}`);
  const data = await res.json();
  if (data.error) throw new Error(data.error.message);
  return data;
}

async function onDownloadClick() {
  const path = document.getElementById('path').value.trim();
  if (!path) { setStatus('保存先フォルダを入力してください。', true); return; }
  if (!pageInfo) { setStatus('ページ情報を取得できていません。', true); return; }

  await saveSettings();
  document.getElementById('download-btn').disabled = true;

  let urls;

  if (pageInfo.type === 'video') {
    urls = [`https://www.youtube.com/watch?v=${pageInfo.id}`];
    setStatus('ダウンロードを開始します...');
  } else {
    const apiKey = document.getElementById('api-key').value.trim();
    if (!apiKey) { setStatus('設定からAPIキーを入力・保存してください。', true); document.getElementById('download-btn').disabled = false; return; }

    const countStr = document.getElementById('count').value.trim();
    const count = countStr ? parseInt(countStr, 10) : null;

    setStatus('動画リストを取得中...');
    try {
      urls = await fetchVideoUrls(pageInfo, apiKey, count);
    } catch (e) {
      setStatus(`エラー: ${e.message}`, true);
      document.getElementById('download-btn').disabled = false;
      return;
    }

    if (urls.length === 0) {
      setStatus('動画が見つかりませんでした。', true);
      document.getElementById('download-btn').disabled = false;
      return;
    }

    setStatus(`${urls.length}件の動画をダウンロードします...`);
  }

  chrome.runtime.onMessage.addListener(handleProgress);
  chrome.runtime.sendMessage({ action: 'startDownload', urls, path });
}

async function fetchVideoUrls(info, apiKey, count) {
  let playlistId;

  if (info.type === 'channel') {
    const data = await apiGet(`channels?part=contentDetails&id=${info.id}&key=${apiKey}`);
    if (!data.items?.length) throw new Error('チャンネルが見つかりません');
    playlistId = data.items[0].contentDetails.relatedPlaylists.uploads;
  } else {
    playlistId = info.id;
  }

  const urls = [];
  let nextPageToken = null;

  do {
    const maxResults = count ? Math.min(count - urls.length, 50) : 50;
    let endpoint = `playlistItems?part=snippet&playlistId=${playlistId}&maxResults=${maxResults}&key=${apiKey}`;
    if (nextPageToken) endpoint += `&pageToken=${nextPageToken}`;

    const data = await apiGet(endpoint);

    for (const item of data.items) {
      const videoId = item.snippet?.resourceId?.videoId;
      if (videoId) urls.push(`https://www.youtube.com/watch?v=${videoId}`);
      if (count && urls.length >= count) break;
    }

    nextPageToken = (count && urls.length >= count) ? null : data.nextPageToken;
  } while (nextPageToken);

  return urls;
}

function handleProgress(message) {
  if (message.action === 'progress') {
    const pct = Math.round((message.current / message.total) * 100);
    document.getElementById('progress-wrap').style.display = 'block';
    document.getElementById('progress-fill').style.width = pct + '%';
    const title = message.title ? `「${message.title}」` : '';
    setStatus(`(${message.current}/${message.total}) ${title} ダウンロード中...`);
  } else if (message.action === 'complete') {
    document.getElementById('progress-fill').style.width = '100%';
    const failNote = message.failed > 0 ? `（失敗 ${message.failed}件）` : '';
    setStatus(`完了！ ${message.downloaded}件ダウンロードしました${failNote}`, false, true);
    document.getElementById('download-btn').disabled = false;
    chrome.runtime.onMessage.removeListener(handleProgress);
  } else if (message.action === 'downloadError') {
    setStatus(`エラー: ${message.error}`, true);
    document.getElementById('download-btn').disabled = false;
    chrome.runtime.onMessage.removeListener(handleProgress);
  }
}

function setStatus(text, isError = false, isSuccess = false) {
  const el = document.getElementById('status');
  el.textContent = text;
  el.className = isError ? 'error' : isSuccess ? 'success' : '';
}

function showNotSupported() {
  document.getElementById('not-supported').style.display = 'block';
}

async function saveSettings() {
  const apiKey = document.getElementById('api-key').value.trim();
  const downloadPath = document.getElementById('path').value.trim();
  await chrome.storage.sync.set({ apiKey, downloadPath });
}
