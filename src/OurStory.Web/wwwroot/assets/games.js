/* 在线五子棋：长轮询等待变更，待确认棋子仅作点击反馈，棋局仍以服务端为准。 */
(() => {
  'use strict';
  const root = document.querySelector('[data-gomoku]');
  if (!root) return;
  const find = name => root.querySelector(`[data-${name}]`);
  const board = find('board');
  const token = root.querySelector('input[name="__RequestVerificationToken"]').value;
  const cells = [];
  let game = null;
  let ready = false;
  let busy = false;
  let polling = false;
  let stopped = false;
  let syncFailed = false;
  let focusPoint = 112;
  let generation = 0;
  let timer;
  let stateRequest = null;
  let forceRefresh = false;
  let pendingPoint = null;
  let suspended = false;
  const colorName = color => color === 1 ? '黑棋' : '白棋';
  const turn = () => (game?.moves.length ?? 0) % 2 + 1;
  const canPlay = () => ready && !busy && game && game.outcome === 0 && game.myColor === turn();
  const error = message => { find('error').textContent = message || ''; find('error').hidden = !message; };

  function winningPoints(moves, outcome) {
    if (!moves.length || (outcome !== 1 && outcome !== 2)) return [];
    const points = new Map(moves.map((point, i) => [point, i % 2 + 1]));
    const last = moves[moves.length - 1];
    // 认输也会有胜方，只有最后一手实际形成连线时才高亮。
    if (points.get(last) !== outcome) return [];
    for (const [dr, dc] of [[0, 1], [1, 0], [1, 1], [1, -1]]) {
      const line = [last];
      for (const sign of [-1, 1]) {
        let row = Math.floor(last / 15) + dr * sign, col = last % 15 + dc * sign;
        while (row >= 0 && row < 15 && col >= 0 && col < 15 && points.get(row * 15 + col) === outcome) {
          line.push(row * 15 + col); row += dr * sign; col += dc * sign;
        }
      }
      if (line.length >= 5) return line;
    }
    return [];
  }

  function render() {
    const moves = game?.moves ?? [];
    const colors = new Map(moves.map((point, i) => [point, i % 2 + 1]));
    if (pendingPoint !== null && !colors.has(pendingPoint)) colors.set(pendingPoint, game.myColor);
    const wins = new Set(winningPoints(moves, game?.outcome));
    const playable = canPlay();
    board.classList.toggle('can-play', !!playable);
    board.setAttribute('aria-busy', String(busy || (!ready && !stopped)));
    cells.forEach((cell, point) => {
      const color = colors.get(point);
      cell.setAttribute('aria-disabled', String(!playable || !!color));
      cell.setAttribute('aria-label', `${Math.floor(point / 15) + 1} 行 ${point % 15 + 1} 列，${color ? colorName(color) : '空位'}`);
      cell.tabIndex = point === focusPoint ? 0 : -1;
      cell.classList.toggle('last', moves.length > 0 && point === moves[moves.length - 1]);
      cell.classList.toggle('winner', wins.has(point));
      cell.classList.toggle('pending', point === pendingPoint);
      if (cell.dataset.color !== (color ? String(color) : undefined)) {
        cell.replaceChildren();
        if (color) {
          cell.dataset.color = String(color);
          const piece = document.createElement('span');
          piece.className = `piece stone-${color === 1 ? 'black' : 'white'}`;
          piece.setAttribute('aria-hidden', 'true');
          cell.append(piece);
        } else delete cell.dataset.color;
      }
    });
    find('move-count').textContent = `${moves.length} 手`;
    const last = moves[moves.length - 1];
    find('last-move').textContent = moves.length ? `上一手：${Math.floor(last / 15) + 1} 行 ${last % 15 + 1} 列` : '黑棋先行';
    find('start').disabled = !ready || busy || !!(game && game.outcome === 0);
    find('start').textContent = game ? '再来一局' : '开始一局';
    find('resign').disabled = !ready || busy || !game || game.outcome !== 0;
    find('refresh').disabled = busy;
    const status = find('status'), hint = find('status-hint');
    if (!ready) {
      status.textContent = stopped ? '暂时无法进入棋局' : '正在连接棋局…';
      hint.textContent = '连接恢复后才能落子，已保存的棋局不会丢失';
    } else if (pendingPoint !== null) {
      status.textContent = '正在确认落子…'; hint.textContent = '棋子已标记，等待服务器确认';
    } else if (!game) {
      status.textContent = '准备好，来一局？'; hint.textContent = '点击「开始一局」，邀请对方打开此页面';
    } else if (game.outcome) {
      status.textContent = game.outcome === 3 ? '这一局，平分秋色' : (game.outcome === game.myColor ? '你赢了这一局！' : '这一局，对方赢了');
      hint.textContent = game.outcome === 3 ? '棋盘已满，和棋' : `${colorName(game.outcome)}获胜 · 再来一局交换先手`;
    } else {
      status.textContent = game.myColor === turn() ? '轮到你落子' : '等待对方落子';
      hint.textContent = `${colorName(turn())}行棋 · 点击棋盘交叉点落子`;
    }
    const displayedColor = game?.outcome === 1 || game?.outcome === 2 ? game.outcome : turn();
    find('turn-stone').className = `turn-stone stone-${displayedColor === 1 ? 'black' : 'white'}`;
    for (const [name, color] of [['my', game?.myColor], ['partner', game ? 3 - game.myColor : null]]) {
      find(`${name}-stone`).className = `player-stone${color ? ` stone-${color === 1 ? 'black' : 'white'}` : ''}`;
      find(`${name}-color`).textContent = color ? `${colorName(color)} · ${color === 1 ? '先手' : '后手'}` : '开局后分配棋色';
    }
  }

  async function request(url, options = {}) {
    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(10000),
      ...options, headers: { 'X-Requested-With': 'XMLHttpRequest', ...options.headers } });
    if (response.status === 401) {
      stopped = true; ready = false; find('login').hidden = false;
      throw new Error('登录已过期，请重新登录后继续。');
    }
    if (response.status === 400) throw new Error('页面凭证已过期，请刷新整个页面后重试。');
    const data = await response.json();
    if (response.status === 403) { stopped = true; ready = false; }
    if (!data.ok) throw new Error(data.message || '操作未完成，请刷新棋局后重试。');
    return data;
  }

  function accept(data) {
    if (!game || !data.game || data.game.version >= game.version) game = data.game;
    ready = true;
    find('connection').textContent = '已同步 · 对方落子后自动更新';
  }

  async function sync(wait = false) {
    if (busy || polling || stopped) return;
    polling = true;
    const current = generation;
    const controller = new AbortController();
    stateRequest = controller;
    const timeout = setTimeout(() => controller.abort(new DOMException('连接超时', 'TimeoutError')), 30000);
    try {
      const query = wait ? `&wait=true&version=${game?.version ?? 0}` : '';
      const data = await request(`/games/gomoku?handler=State${query}`, { signal: controller.signal });
      if (current !== generation) return;
      accept(data);
      if (syncFailed) error('');
      syncFailed = false;
    } catch (e) {
      if (current !== generation || e.name === 'AbortError') return;
      ready = false;
      syncFailed = true;
      find('connection').textContent = stopped ? '连接已暂停' : '连接中断，正在自动重连…';
      error(e instanceof TypeError || e.name === 'TimeoutError' ? '暂时无法连接服务器，请检查网络。' : e.message);
    } finally {
      clearTimeout(timeout);
      if (stateRequest === controller) stateRequest = null;
      polling = false;
      render();
    }
  }

  async function act(action, point = -1) {
    if (busy || !ready) return;
    busy = true; generation++;
    stateRequest?.abort();
    pendingPoint = action === 'move' ? point : null;
    error(''); render();
    const payload = new URLSearchParams({ action, version: String(game?.version ?? 0),
      row: String(Math.floor(point / 15)), col: String(point % 15), __RequestVerificationToken: token });
    try {
      accept(await request('/games/gomoku?handler=Online', { method: 'POST', body: payload }));
    } catch (e) {
      error(e instanceof TypeError || e.name === 'TimeoutError' ? '未确认操作结果，正在重新读取棋局，请勿重复落子。' : e.message);
      ready = false;
    } finally {
      pendingPoint = null;
      busy = false; render();
      schedule(0);
    }
  }

  for (let point = 0; point < 225; point++) {
    const cell = document.createElement('button');
    cell.type = 'button'; cell.className = 'board-point';
    if ([48, 56, 112, 168, 176].includes(point)) cell.classList.add('star');
    cell.addEventListener('focus', () => { cells[focusPoint].tabIndex = -1; focusPoint = point; cell.tabIndex = 0; });
    cell.addEventListener('click', () => {
      if (canPlay() && !cell.dataset.color) void act('move', point);
    });
    cell.addEventListener('keydown', event => {
      let next = point;
      if (event.key === 'ArrowLeft') next = point % 15 ? point - 1 : point;
      else if (event.key === 'ArrowRight') next = point % 15 < 14 ? point + 1 : point;
      else if (event.key === 'ArrowUp') next = Math.max(0, point - 15);
      else if (event.key === 'ArrowDown') next = Math.min(224, point + 15);
      else return;
      event.preventDefault(); cells[next].focus();
    });
    board.append(cell); cells.push(cell);
  }
  find('start').addEventListener('click', () => void act('start'));
  find('resign').addEventListener('click', () => {
    if (window.confirm('认输后本局结束，对方获胜。确定认输吗？')) void act('resign');
  });
  find('refresh').addEventListener('click', () => {
    error(''); stopped = false; forceRefresh = true; stateRequest?.abort(); schedule(0);
  });
  find('invite').addEventListener('click', async () => {
    const link = new URL('/games/gomoku', window.location.origin).href;
    try { await navigator.clipboard.writeText(link); find('connection').textContent = '链接已复制，让对方用自己的账号打开即可'; }
    catch { window.prompt('复制此链接，发给对方：', link); }
  });
  function schedule(delay) {
    clearTimeout(timer);
    if (!suspended) timer = setTimeout(poll, delay);
  }
  async function poll() {
    if (suspended) return;
    if (document.hidden || busy || polling || stopped) { schedule(1000); return; }
    const wait = ready && !forceRefresh;
    forceRefresh = false;
    await sync(wait);
    // 成功后立即重新等待，只在失败时退避，避免断线时请求风暴。
    schedule(ready ? 0 : 1500);
  }
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) stateRequest?.abort();
    else { forceRefresh = true; schedule(0); }
  });
  window.addEventListener('pagehide', () => { suspended = true; clearTimeout(timer); stateRequest?.abort(); });
  window.addEventListener('pageshow', () => { suspended = false; forceRefresh = true; schedule(0); });
  render(); schedule(0);
})();
