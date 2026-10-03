/* 私密画猜：答案只由服务端按角色提供，画笔以小段增量同步。 */
(() => {
  'use strict';
  const root = document.querySelector('[data-draw-game]');
  if (!root) return;
  const find = name => root.querySelector(`[data-${name}]`);
  const canvas = find('canvas'), overlay = find('overlay');
  const ctx = canvas.getContext('2d'), preview = overlay.getContext('2d');
  const token = root.querySelector('[name="__RequestVerificationToken"]').value;
  const colors = ['#30303b', '#df728d', '#e85a50', '#e6a23c', '#55a878', '#4a8ecc', '#916bbf', '#ffffff'];
  const colorNames = ['墨黑', '粉红', '朱红', '暖黄', '草绿', '蓝色', '紫色', '白色'];
  let game = null, strokes = [], epoch = '', queue = [], sending = null;
  let connected = false, stopped = false, suspended = false, polling = false;
  let stateController = null, pollTimer, retryTimer, forceRefresh = true;
  let brush = colors[0], eraser = false, active = null, pointer = null;
  let clockValue = Date.now(), clockReadAt = performance.now();
  let choicesKey = '', guessesKey = '', resultsKey = '';
  const pending = new Map();
  const uuid = () => crypto.randomUUID ? crypto.randomUUID() : '10000000-1000-4000-8000-100000000000'.replace(/[018]/g, c => (c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> c / 4).toString(16));
  const error = message => { find('error').textContent = message || ''; find('error').hidden = !message; };
  const now = () => clockValue + performance.now() - clockReadAt;
  const remaining = () => game?.endsAt ? Math.max(0, Math.ceil((Date.parse(game.endsAt) - now()) / 1000)) : 90;
  const controlBusy = () => (sending && sending.action !== 'ink') || queue.some(x => x.action !== 'ink');
  const canDraw = () => connected && !stopped && !controlBusy() && game?.isDrawer && game.stage === 'drawing' && remaining() > 0 && queue.length < 60;

  function paint(context, stroke) {
    const points = stroke.points;
    if (!points.length) return;
    context.strokeStyle = stroke.eraser ? '#ffffff' : stroke.color;
    context.fillStyle = context.strokeStyle;
    context.lineWidth = stroke.width;
    context.lineCap = 'round'; context.lineJoin = 'round';
    if (points.length === 1) {
      context.beginPath(); context.arc(points[0].x, points[0].y, stroke.width / 2, 0, Math.PI * 2); context.fill();
    } else {
      context.beginPath(); context.moveTo(points[0].x, points[0].y);
      for (const point of points.slice(1)) context.lineTo(point.x, point.y);
      context.stroke();
    }
  }
  function repaint() {
    ctx.fillStyle = '#ffffff'; ctx.fillRect(0, 0, 1000, 700);
    for (const stroke of strokes) paint(ctx, stroke);
    paintPreview();
  }
  function paintPreview() {
    preview.clearRect(0, 0, 1000, 700);
    for (const stroke of pending.values()) paint(preview, stroke);
    if (active) paint(preview, active);
  }
  function stopPointer() {
    active = null;
    if (pointer !== null && overlay.hasPointerCapture(pointer)) overlay.releasePointerCapture(pointer);
    pointer = null;
    paintPreview();
  }

  function accept(response) {
    const next = response.game;
    if (game && (!next || next.version < game.version)) return;
    if (!next) {
      game = null; strokes = []; epoch = ''; pending.clear(); queue = []; stopPointer(); repaint();
    } else {
      const reset = next.canvasEpoch !== epoch;
      if (reset) { strokes = []; epoch = next.canvasEpoch; pending.clear(); stopPointer(); }
      if (next.strokeBase > strokes.length) {
        // 缺了中间一段，下一次读取完整画布，不能拼出一个看似完整的错误画面。
        forceRefresh = true; epoch = ''; schedule(0); return;
      }
      strokes = strokes.slice(0, next.strokeBase).concat(next.strokes);
      for (const stroke of next.strokes) pending.delete(stroke.id);
      game = next;
      clockValue = Date.parse(next.serverNow); clockReadAt = performance.now();
      if (next.stage !== 'drawing') { pending.clear(); stopPointer(); }
      queue = queue.filter(command => command.roundId === next.roundId
        && (command.action !== 'ink' || (command.canvasEpoch === epoch && next.stage === 'drawing')));
      repaint();
    }
    connected = true;
    find('connection').textContent = '已连接 · 画笔和猜测实时同步';
    render();
  }

  function render() {
    const stage = game?.stage ?? 'idle';
    const isDrawer = !!game?.isDrawer;
    const busy = !!controlBusy();
    find('round').textContent = game ? `第 ${game.round} / 6 轮` : '准备开始';
    find('score').textContent = `共同猜中 ${game?.score ?? 0} / 6`;
    find('role').textContent = game ? (isDrawer ? '这一轮 · 我来画' : '这一轮 · 我来猜') : '两个人的画室';
    find('secret').hidden = !(isDrawer && stage === 'drawing');
    find('answer').textContent = isDrawer && stage === 'drawing' ? game.answer : '';
    find('toolbar').hidden = !(isDrawer && stage === 'drawing');
    overlay.classList.toggle('can-draw', !!canDraw());
    find('width').disabled = !canDraw(); find('eraser').disabled = !canDraw(); find('clear').disabled = !canDraw();
    find('colors').querySelectorAll('button').forEach(button => { button.disabled = !canDraw(); });
    find('start').disabled = !connected || busy || (stage !== 'idle' && stage !== 'finished');
    find('start').textContent = stage === 'finished' ? '再挑战 6 轮' : '开始 6 轮挑战';
    find('end').disabled = !connected || busy || stage === 'idle' || stage === 'finished';
    find('next').disabled = !connected || busy;
    find('next').textContent = game?.round === 6 ? '查看这一局成绩' : '交换角色，下一轮';
    const guessable = connected && stage === 'drawing' && !isDrawer && remaining() > 0 && !busy;
    root.querySelector('#draw-guess').disabled = !guessable;
    find('guess-button').disabled = !guessable;
    find('guess-help').textContent = isDrawer && stage === 'drawing' ? '对方的猜测会实时显示在这里。' : '可多次尝试；答案忽略空白与大小写，配置的别名也算猜中。';
    const status = find('status'), hint = find('hint');
    if (!connected) {
      status.textContent = stopped ? '暂时无法进入画室' : '正在连接画室…'; hint.textContent = '连接恢复后继续，已同步的画作不会丢失。';
    } else if (stage === 'idle') {
      status.textContent = '准备好，让默契开场'; hint.textContent = '点击开始挑战，另一方打开此页面就能加入。';
    } else if (stage === 'choosing') {
      status.textContent = isDrawer ? '选一个词，画给对方猜' : '对方正在悄悄选词';
      hint.textContent = isDrawer ? '选定后开始 90 秒计时。想好了再下笔。' : '选好词后画布会自动开放，你不需要刷新。';
    } else if (stage === 'drawing') {
      status.textContent = isDrawer ? '画出你的奇思妙想' : '看着画，猜猜你想到的词';
      hint.textContent = game.hintCategory ? `半程提示：${game.hintCategory} · ${game.hintLength} 个字` : '45 秒后会出现分类和字数提示。';
    } else if (stage === 'reveal') {
      const result = game.results.find(x => x.round === game.round);
      status.textContent = result?.correct ? '猜中了，我们很有默契！' : '时间到，看看这轮的答案';
      hint.textContent = '这一幅画先留在这里，准备好了再继续。';
    } else {
      status.textContent = `这一局，共同猜中 ${game.score} 题`; hint.textContent = '画得像不像都没关系，我们又多了一点默契。';
    }
    find('canvas-note').hidden = stage === 'drawing' || strokes.length > 0;
    find('canvas-note').textContent = stage === 'choosing' ? (isDrawer ? '选好词，就可以开画了' : '等对方选好词，画作会出现在这里') : stage === 'finished' ? '谢谢你陪我画完这一局' : '一方画出线索，一方猜出答案';
    find('reveal').hidden = stage !== 'reveal';
    find('reveal-answer').textContent = stage === 'reveal' ? game.answer : '';
    find('reveal-result').textContent = game?.results.find(x => x.round === game.round)?.correct ? '你画的，我猜到了！本轮 +1' : '这轮没有猜中，下轮再接再厉';
    const nextChoices = JSON.stringify(game?.choices ?? []);
    find('choices').hidden = !(stage === 'choosing' && isDrawer);
    if (nextChoices !== choicesKey) {
      choicesKey = nextChoices; find('choices').replaceChildren();
      for (const choice of game?.choices ?? []) {
        const button = document.createElement('button'); button.type = 'button'; button.className = 'draw-choice';
        const label = document.createElement('strong'); label.textContent = choice.answer;
        const meta = document.createElement('small'); meta.textContent = `${choice.category} · ${['', '简单', '适中', '挑战'][choice.difficulty]}`;
        button.append(label, meta); button.addEventListener('click', () => enqueue('choose', { choice: choice.index })); find('choices').append(button);
      }
    }
    find('choices').querySelectorAll('button').forEach(button => { button.disabled = !connected || busy; });
    const nextGuesses = JSON.stringify(game?.guesses ?? []);
    if (nextGuesses !== guessesKey) {
      guessesKey = nextGuesses; find('guesses').replaceChildren();
      if (!game?.guesses.length) { const p = document.createElement('p'); p.className = 'draw-empty'; p.textContent = '猜测会出现在这里。'; find('guesses').append(p); }
      for (const guess of game?.guesses ?? []) {
        const p = document.createElement('p'); p.textContent = guess.text + (guess.correct ? ' · 猜中了！' : '');
        if (guess.correct) p.className = 'correct'; find('guesses').append(p);
      }
      find('guesses').scrollTop = find('guesses').scrollHeight;
    }
    find('summary').hidden = !game?.results.length;
    const nextResults = JSON.stringify(game?.results ?? []);
    if (nextResults !== resultsKey) {
      resultsKey = nextResults; find('results').replaceChildren();
      for (const result of game?.results ?? []) { const li = document.createElement('li'); li.textContent = `${result.answer} · ${result.correct ? '猜中 +1' : '未猜中'}`; find('results').append(li); }
    }
    find('ink-state').textContent = pending.size ? `${pending.size} 段笔画同步中…` : '画布与猜测自动保存';
    updateTimer();
  }
  function updateTimer() {
    const seconds = remaining();
    find('timer').textContent = game?.stage === 'drawing' ? `${seconds} 秒` : game?.stage === 'reveal' ? '本轮结束' : game?.stage === 'finished' ? '挑战完成' : '90 秒';
    find('timer').classList.toggle('urgent', game?.stage === 'drawing' && seconds <= 15);
    if (game?.stage === 'drawing' && seconds === 0) {
      overlay.classList.remove('can-draw'); root.querySelector('#draw-guess').disabled = true; find('guess-button').disabled = true;
    }
  }

  async function read(response) {
    if (response.status === 401 || response.redirected) {
      stopped = true; connected = false; find('login').hidden = false;
      throw Object.assign(new Error('登录已过期，请重新登录。'), { permanent: true });
    }
    if (response.status === 403) { stopped = true; connected = false; }
    let data;
    try { data = await response.json(); } catch { throw Object.assign(new Error('请求未完成，请刷新页面后重试。'), { permanent: response.status === 400 }); }
    if (!response.ok || !data.ok) {
      if (data.game) accept(data);
      throw Object.assign(new Error(data.message || '操作未完成，请刷新页面后重试。'), { permanent: response.status >= 400 && response.status < 500 });
    }
    return data;
  }
  async function sync(wait) {
    if (polling || stopped || suspended) return;
    polling = true;
    const controller = new AbortController(); stateController = controller;
    const timeout = setTimeout(() => controller.abort(new DOMException('连接超时', 'TimeoutError')), 30000);
    try {
      const query = new URLSearchParams({ handler: 'State', version: String(game?.version ?? 0), wait: String(wait), epoch, since: String(strokes.length) });
      const response = await fetch(`/games/draw?${query}`, { signal: controller.signal, credentials: 'same-origin', cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
      const wasConnected = connected;
      accept(await read(response));
      if (!wasConnected) error('');
      void pump();
    } catch (e) {
      if (e.name !== 'AbortError') {
        connected = false; find('connection').textContent = stopped ? '连接已暂停' : '连接中断，正在自动重连…';
        error(e instanceof TypeError || e.name === 'TimeoutError' ? '暂时连不上服务器，已同步的画作会保留。' : e.message);
        render();
      }
    } finally { clearTimeout(timeout); polling = false; if (stateController === controller) stateController = null; }
  }
  function schedule(delay) { clearTimeout(pollTimer); if (!suspended) pollTimer = setTimeout(poll, delay); }
  async function poll() {
    if (suspended) return;
    if (document.hidden || polling || stopped) { schedule(1000); return; }
    const wait = connected && !forceRefresh; forceRefresh = false;
    await sync(wait); schedule(connected ? 0 : 1500);
  }

  function enqueue(action, extra = {}) {
    if (!connected || stopped || (action !== 'ink' && controlBusy())) return;
    error('');
    const command = { action, requestId: uuid(), roundId: game?.roundId ?? '', canvasEpoch: epoch, ...extra };
    if (command.stroke) command.stroke.id = command.requestId;
    queue.push(command);
    if (command.stroke) pending.set(command.requestId, command.stroke);
    render(); paintPreview(); void pump();
  }
  async function pump() {
    if (sending || !connected || stopped || suspended || !queue.length) return;
    const command = queue.shift();
    if (command.action !== 'start' && command.roundId !== game?.roundId) { pending.delete(command.requestId); void pump(); return; }
    sending = command; render();
    try {
      const response = await fetch('/games/draw?handler=Act', { method: 'POST', credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(10000),
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token, 'X-Requested-With': 'XMLHttpRequest' },
        body: JSON.stringify({ ...command, canvasSince: strokes.length }) });
      accept(await read(response));
      pending.delete(command.requestId);
    } catch (e) {
      if (e.permanent) {
        pending.delete(command.requestId); error(e.message);
        forceRefresh = true; stateController?.abort(); schedule(0);
      } else {
        // 复用相同 requestId，响应丢失时重试也不会重复落笔或重复计分。
        queue.unshift(command); connected = false; error('操作尚未确认，恢复连接后会自动重试。');
        find('connection').textContent = '连接中断，正在自动重连…';
        forceRefresh = true; stateController?.abort(); schedule(1000);
      }
    } finally {
      sending = null; render(); paintPreview();
      if (command.action === 'guess' && !root.querySelector('#draw-guess').disabled)
        root.querySelector('#draw-guess').focus({ preventScroll: true });
      clearTimeout(retryTimer); retryTimer = setTimeout(pump, connected ? 0 : 1500);
    }
  }

  function point(event) {
    const rect = overlay.getBoundingClientRect();
    return { x: Math.round(Math.max(0, Math.min(1000, (event.clientX - rect.left) / rect.width * 1000))),
      y: Math.round(Math.max(0, Math.min(700, (event.clientY - rect.top) / rect.height * 700))) };
  }
  function flush(final = false) {
    if (!active || (!final && active.points.length < 2)) return;
    if (!connected || queue.length >= 60) { error('网络暂时跟不上，等笔画同步完成再继续画。'); stopPointer(); return; }
    const segment = { ...active, points: active.points.slice() };
    const last = active.points[active.points.length - 1];
    active = final ? null : { ...active, points: [last] };
    enqueue('ink', { stroke: segment });
  }
  overlay.addEventListener('pointerdown', event => {
    if (!canDraw() || pointer !== null || (event.pointerType === 'mouse' && event.button !== 0)) return;
    event.preventDefault(); pointer = event.pointerId; overlay.setPointerCapture(pointer);
    active = { id: '', color: brush, width: Number(find('width').value), eraser, points: [point(event)] };
    paintPreview();
  });
  overlay.addEventListener('pointermove', event => {
    if (!active || pointer !== event.pointerId) return;
    if (!canDraw()) { flush(true); stopPointer(); return; }
    const next = point(event), last = active.points[active.points.length - 1];
    if (Math.hypot(next.x - last.x, next.y - last.y) < 2) return;
    active.points.push(next);
    if (active.points.length >= 32) flush();
    paintPreview();
  });
  const finishPointer = event => { if (pointer !== event.pointerId) return; flush(true); stopPointer(); };
  overlay.addEventListener('pointerup', finishPointer);
  overlay.addEventListener('pointercancel', finishPointer);
  overlay.addEventListener('lostpointercapture', finishPointer);
  const flushTimer = setInterval(() => flush(), 120);
  colors.forEach((color, i) => {
    const button = document.createElement('button'); button.type = 'button'; button.className = 'draw-color'; button.style.backgroundColor = color;
    button.setAttribute('aria-label', colorNames[i]); button.setAttribute('aria-pressed', String(i === 0));
    button.addEventListener('click', () => { brush = color; eraser = false; find('eraser').setAttribute('aria-pressed', 'false');
      find('colors').querySelectorAll('button').forEach(item => item.setAttribute('aria-pressed', String(item === button))); });
    find('colors').append(button);
  });
  find('eraser').addEventListener('click', () => { eraser = !eraser; find('eraser').setAttribute('aria-pressed', String(eraser)); });
  find('clear').addEventListener('click', () => { if (confirm('清空本轮画布？对方的画面也会一起清空。')) { flush(true); stopPointer(); enqueue('clear'); } });
  find('start').addEventListener('click', () => enqueue('start'));
  find('next').addEventListener('click', () => enqueue('next'));
  find('end').addEventListener('click', () => { if (confirm('结束整局挑战？当前轮会结束，已猜中的成绩会保留。')) { flush(true); stopPointer(); enqueue('end'); } });
  find('guess-form').addEventListener('submit', event => { event.preventDefault(); const input = root.querySelector('#draw-guess');
    if (input.disabled || !input.value.trim()) return; enqueue('guess', { text: input.value }); input.value = ''; });
  find('refresh').addEventListener('click', () => { stopped = false; error(''); forceRefresh = true; stateController?.abort(); schedule(0); });
  find('invite').addEventListener('click', async () => {
    const link = new URL('/games/draw', location.origin).href;
    try { await navigator.clipboard.writeText(link); find('connection').textContent = '邀请链接已复制，请对方用自己的账号打开'; }
    catch { window.prompt('复制此链接发给对方：', link); }
  });
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) { flush(true); stopPointer(); stateController?.abort(); }
    else { forceRefresh = true; schedule(0); }
  });
  const timerInterval = setInterval(updateTimer, 200);
  window.addEventListener('pagehide', () => { suspended = true; clearTimeout(pollTimer); clearTimeout(retryTimer); stateController?.abort(); });
  window.addEventListener('pageshow', () => { suspended = false; forceRefresh = true; schedule(0); void pump(); });
  // 定时器只操作本页状态，后台页不会发送空笔画。
  void flushTimer; void timerInterval;
  repaint(); render(); schedule(0);
})();
