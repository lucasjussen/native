namespace Native.Protocol.Share;

/// <summary>A página que o iPhone abre (Safari ou Tela de Início). Sem dependências externas.</summary>
public static class SharePage
{
    public const string Html = """
<!DOCTYPE html>
<html lang="pt-BR">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
<meta name="apple-mobile-web-app-title" content="Native">
<meta name="format-detection" content="telephone=no">
<meta name="theme-color" content="#000000" media="(prefers-color-scheme: dark)">
<meta name="theme-color" content="#f2f2f7" media="(prefers-color-scheme: light)">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
<title>Native</title>
<style>
:root{
  --bg:#f2f2f7; --card:#fff; --card2:#f2f2f7; --text:#000; --sec:#6c6c70; --ter:#aeaeb2; --sep:rgba(60,60,67,.16);
  --blue:#007aff; --green:#34c759; --red:#ff3b30; --fill:rgba(120,120,128,.12); --shadow:0 1px 2px rgba(0,0,0,.04);
}
@media (prefers-color-scheme: dark){
  :root{ --bg:#000; --card:#1c1c1e; --card2:#2c2c2e; --text:#fff; --sec:#98989f; --ter:#636366; --sep:rgba(84,84,88,.55);
         --blue:#0a84ff; --green:#30d158; --red:#ff453a; --fill:rgba(120,120,128,.24); --shadow:none; }
}
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
html,body{margin:0;background:var(--bg);color:var(--text);
  font:17px/1.35 -apple-system,BlinkMacSystemFont,"SF Pro Text",system-ui,sans-serif;-webkit-font-smoothing:antialiased}
body{padding:calc(env(safe-area-inset-top) + 14px) 16px calc(env(safe-area-inset-bottom) + 96px)}
button{font:inherit;color:inherit}
.hidden{display:none !important}

/* topo */
.top{display:flex;align-items:center;gap:14px;margin:6px 4px 22px}
.logo{width:52px;height:52px;border-radius:12px;flex:none;box-shadow:0 0 0 .5px var(--sep)}
h1{font-size:30px;font-weight:700;letter-spacing:.2px;margin:0;line-height:1.1}
.status{display:inline-flex;align-items:center;gap:7px;margin-top:4px;font-size:14px;color:var(--sec)}
.dot{width:8px;height:8px;border-radius:50%;background:var(--ter);flex:none;transition:background .3s}
.dot.on{background:var(--green);box-shadow:0 0 0 3px rgba(52,199,89,.18)}

/* avisos */
.alert{border-radius:14px;padding:13px 15px;font-size:15px;margin:0 0 14px;background:var(--card);color:var(--red);box-shadow:var(--shadow)}
.tip{display:flex;gap:12px;align-items:flex-start;background:var(--card);border-radius:14px;padding:13px 14px;font-size:14.5px;color:var(--sec);margin-bottom:6px;box-shadow:var(--shadow)}
.tip b{color:var(--text);font-weight:600}
.tip .close{margin-left:auto;border:0;background:none;color:var(--ter);font-size:20px;line-height:1;padding:0 2px}

/* seções */
section{margin-top:24px}
.sec-head{display:flex;align-items:baseline;justify-content:space-between;margin:0 6px 8px}
h2{font-size:20px;font-weight:700;margin:0;letter-spacing:.2px;display:flex;align-items:center;gap:8px}
.badge{font-size:12px;font-weight:700;color:#fff;background:var(--blue);border-radius:999px;padding:2px 8px;letter-spacing:0}
.link{border:0;background:none;color:var(--blue);font-size:15px;font-weight:500;padding:4px 0}
.card{background:var(--card);border-radius:14px;overflow:hidden;box-shadow:var(--shadow)}

/* linhas */
.row{display:flex;align-items:center;gap:12px;padding:10px 12px 10px 12px;min-height:66px;position:relative;transition:background .2s}
.row + .row::before{content:"";position:absolute;top:0;left:72px;right:0;border-top:.5px solid var(--sep)}
.thumb{width:46px;height:46px;border-radius:10px;flex:none;object-fit:cover;background:var(--fill);display:flex;align-items:center;justify-content:center;
  font-size:10.5px;font-weight:700;letter-spacing:.3px;color:#fff;text-transform:uppercase;position:relative}
.thumb.pdf{background:#ff3b30}.thumb.doc{background:#0a84ff}.thumb.xls{background:#30b158}.thumb.ppt{background:#ff9500}
.thumb.zip{background:#8e8e93}.thumb.aud{background:#ff2d55}.thumb.vid{background:#5e5ce6}.thumb.txt{background:#636366}.thumb.lnk{background:#0a84ff}
.thumb.gen{background:var(--fill);color:var(--sec)}
.info{flex:1;min-width:0}
.name{font-size:16px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.meta{font-size:13px;color:var(--sec);margin-top:2px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.meta .ok{color:var(--green);font-weight:600}
.meta .err{color:var(--red)}
.txt{font-size:15px;white-space:pre-wrap;word-break:break-word;display:-webkit-box;-webkit-line-clamp:3;-webkit-box-orient:vertical;overflow:hidden}
.row.done .name,.row.done .txt{color:var(--sec)}

/* ação à direita */
.act{flex:none;min-width:76px;display:flex;justify-content:flex-end}
.pill{border:0;border-radius:999px;background:var(--fill);color:var(--blue);font-size:15px;font-weight:600;padding:7px 15px;text-decoration:none;display:inline-block}
.pill.primary{background:var(--blue);color:#fff}
.ring{width:34px;height:34px;position:relative}
.ring svg{width:34px;height:34px;transform:rotate(-90deg)}
.ring .bg{stroke:var(--fill)}
.ring .fg{stroke:var(--blue);transition:stroke-dashoffset .2s}
.ring span{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;font-size:9.5px;font-weight:700;color:var(--blue)}
.check{width:30px;height:30px;border-radius:50%;background:var(--green);display:flex;align-items:center;justify-content:center;animation:pop .35s cubic-bezier(.2,.9,.3,1.3)}
.check svg{width:16px;height:16px}
@keyframes pop{from{transform:scale(.4);opacity:0}to{transform:none;opacity:1}}
@keyframes enter{from{opacity:0;transform:translateY(6px)}to{opacity:1;transform:none}}
.row.new{animation:enter .35s ease-out}
.empty{color:var(--sec);text-align:center;padding:28px 20px;font-size:15px;line-height:1.45}
.empty svg{display:block;margin:0 auto 10px;width:34px;height:34px;color:var(--ter)}

/* enviar */
.drop{display:flex;align-items:center;gap:14px;width:100%;padding:16px;border:0;background:var(--card);border-radius:14px;color:var(--text);text-align:left;box-shadow:var(--shadow)}
.drop .ico{width:46px;height:46px;border-radius:12px;background:var(--blue);display:flex;align-items:center;justify-content:center;flex:none}
.drop .ico svg{width:24px;height:24px}
.drop b{display:block;font-size:16px;font-weight:600}
.drop small{display:block;color:var(--sec);font-size:13px;margin-top:2px}
.compose{margin-top:10px;display:flex;align-items:flex-end;gap:8px;padding:6px 6px 6px 14px}
textarea{flex:1;border:0;resize:none;background:transparent;color:var(--text);font:16px -apple-system,system-ui;padding:9px 0;min-height:40px;outline:none}
.send{border:0;width:36px;height:36px;border-radius:50%;background:var(--blue);display:flex;align-items:center;justify-content:center;flex:none;margin-bottom:2px}
.send:disabled{opacity:.35}
.send svg{width:18px;height:18px}

/* atalho */
.steps{padding:4px 16px 16px;font-size:15px}
.steps ol{padding-left:20px;margin:4px 0 14px;line-height:1.55}
.chev{color:var(--ter);font-size:20px;transition:transform .2s}
.open .chev{transform:rotate(90deg)}

/* aviso flutuante */
.toast{position:fixed;left:16px;right:16px;bottom:calc(env(safe-area-inset-bottom) + 16px);display:flex;align-items:center;gap:12px;
  padding:13px 16px;border-radius:16px;background:rgba(28,28,30,.94);color:#fff;font-size:15px;box-shadow:0 10px 30px rgba(0,0,0,.25);
  -webkit-backdrop-filter:saturate(180%) blur(20px);backdrop-filter:saturate(180%) blur(20px);transform:translateY(140%);transition:transform .35s cubic-bezier(.2,.9,.3,1)}
.toast.show{transform:none}
.toast .ti{width:26px;height:26px;border-radius:50%;flex:none;display:flex;align-items:center;justify-content:center}
.toast .ti.ok{background:#30d158}
.toast .ti.busy{border:2.5px solid rgba(255,255,255,.25);border-top-color:#fff;animation:spin .8s linear infinite}
.toast small{display:block;color:rgba(255,255,255,.65);font-size:13px;margin-top:1px}
@keyframes spin{to{transform:rotate(360deg)}}
.foot{color:var(--ter);font-size:12.5px;text-align:center;margin-top:30px;padding:0 24px;line-height:1.5}
</style>
</head>
<body>

<header class="top">
  <img class="logo" src="/apple-touch-icon.png" alt="" onerror="this.style.visibility='hidden'">
  <div>
    <h1>Native</h1>
    <div class="status"><span class="dot" id="dot"></span><span id="status">Conectando…</span></div>
  </div>
</header>

<div id="pair" class="alert hidden">Este endereço não está pareado. No computador, abra o Native → <b>Arquivos</b> e escaneie o QR code de novo com a Câmera.</div>
<div id="offline" class="alert hidden">Não encontrei o computador. Confira se o iPhone está no mesmo Wi-Fi e se o Native está aberto no PC.</div>

<div id="hint" class="tip hidden">
  <div>Para usar como app: toque em <b>Compartilhar</b> e depois em <b>Adicionar à Tela de Início</b>.</div>
  <button class="close" id="hintClose" aria-label="Fechar">×</button>
</div>

<section>
  <div class="sec-head">
    <h2>Do computador <span class="badge hidden" id="badge"></span></h2>
    <span>
      <button class="link hidden" id="allBtn">Baixar tudo</button>
      <button class="link hidden" id="clearBtn">Limpar baixados</button>
    </span>
  </div>
  <div class="card" id="inbox"></div>
</section>

<section>
  <div class="sec-head"><h2>Enviar para o computador</h2></div>
  <label class="drop" for="picker">
    <span class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 16V4"/><path d="m7 9 5-5 5 5"/><path d="M5 14v4a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-4"/></svg></span>
    <span><b>Fotos, vídeos ou arquivos</b><small>Chegam na pasta Downloads › Native do PC</small></span>
  </label>
  <input type="file" id="picker" multiple class="hidden">
  <div class="card hidden" id="uploads" style="margin-top:10px"></div>
  <div class="card compose">
    <textarea id="text" rows="1" placeholder="Texto ou link para o computador"></textarea>
    <button class="send" id="sendText" aria-label="Enviar texto" disabled><svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 19V5"/><path d="m5 12 7-7 7 7"/></svg></button>
  </div>
</section>

<section id="sc">
  <div class="card">
    <div class="row" id="scHead" style="cursor:pointer;min-height:58px">
      <div class="info"><div class="name" style="font-weight:600">Atalho “Enviar para o PC”</div>
        <div class="meta">Compartilhar → Enviar para o PC, como o AirDrop.</div></div>
      <span class="chev">›</span>
    </div>
    <div class="steps hidden" id="scSteps">
      <ol>
        <li>Toque em <b>Copiar endereço</b> abaixo.</li>
        <li>App <b>Atalhos</b> → <b>+</b>. Nome: <b>Enviar para o PC</b>.</li>
        <li>No bloco <b>Receber</b> do topo, troque <b>Lugar Nenhum</b> por <b>Planilha de Compartilhamento</b>.</li>
        <li>Adicione <b>Repetir com Cada</b> → <b>Entrada do Atalho</b>.</li>
        <li>Dentro dele, <b>Obter Conteúdo do URL</b>: cole o endereço; Método <b>POST</b>; Cabeçalho <b>X-Filename</b> = <b>Item da Repetição › Nome</b>; Corpo <b>Arquivo</b> = <b>Item da Repetição</b>.</li>
      </ol>
      <button class="pill primary" id="scCopy">Copiar endereço</button>
      <div class="meta" id="scHost" style="white-space:normal;word-break:break-all;margin-top:8px"></div>
    </div>
  </div>
</section>

<p class="foot">Mantenha esta tela aberta para ver na hora o que o PC mandar. Funciona só no mesmo Wi-Fi do computador.</p>

<div class="toast" id="toast" role="status" aria-live="polite"><span class="ti" id="toastIcon"></span><div><span id="toastText"></span><small id="toastSub"></small></div></div>

<script>
(() => {
  const key = new URLSearchParams(location.search).get('k') || '';
  const $ = id => document.getElementById(id);
  const q = (p, extra) => `${p}?k=${encodeURIComponent(key)}${extra || ''}`;
  const CHECK = '<svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 12.5 10 17 19 7"/></svg>';
  let failures = 0, busy = false, shortcutUrl = '', active = 0, items = [], firstLoad = true;
  const rows = new Map();

  // baixados ficam lembrados neste iPhone (além do que o PC sabe)
  let doneIds;
  try { doneIds = new Set(JSON.parse(localStorage.getItem('native.done') || '[]')); } catch (e) { doneIds = new Set(); }
  const saveDone = () => { try { localStorage.setItem('native.done', JSON.stringify([...doneIds].slice(-400))); } catch (e) {} };
  const isDone = it => doneIds.has(it.id) || it.delivered;

  if (!navigator.standalone && !localStorage.getItem('hintClosed')) $('hint').classList.remove('hidden');
  $('hintClose').onclick = () => { $('hint').classList.add('hidden'); try { localStorage.setItem('hintClosed', '1'); } catch (e) {} };

  // ---------- utilidades
  const fmtSize = n => n < 1024 ? n + ' B' : n < 1048576 ? (n / 1024).toFixed(0) + ' KB'
    : n < 1073741824 ? (n / 1048576).toFixed(1).replace('.', ',') + ' MB' : (n / 1073741824).toFixed(2).replace('.', ',') + ' GB';
  const ago = ms => { const s = Math.max(0, (Date.now() - ms) / 1000);
    return s < 60 ? 'agora' : s < 3600 ? `há ${Math.floor(s / 60)} min` : s < 86400 ? `há ${Math.floor(s / 3600)} h` : new Date(ms).toLocaleDateString('pt-BR'); };
  const ext = n => { const p = n.lastIndexOf('.'); return p > 0 ? n.slice(p + 1).toLowerCase() : ''; };
  const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const kind = (name, type) => {
    const e = ext(name);
    if (type.startsWith('image/')) return 'img';
    if (type.startsWith('video/')) return 'vid';
    if (type.startsWith('audio/')) return 'aud';
    if (e === 'pdf') return 'pdf';
    if (['doc', 'docx', 'pages', 'rtf', 'odt'].includes(e)) return 'doc';
    if (['xls', 'xlsx', 'csv', 'numbers', 'ods'].includes(e)) return 'xls';
    if (['ppt', 'pptx', 'key', 'odp'].includes(e)) return 'ppt';
    if (['zip', 'rar', '7z', 'gz'].includes(e)) return 'zip';
    if (['txt', 'md', 'json'].includes(e)) return 'txt';
    return 'gen';
  };
  function copy(text) {
    const t = document.createElement('textarea');
    t.value = text; t.setAttribute('readonly', ''); t.style.position = 'fixed'; t.style.opacity = '0';
    document.body.appendChild(t); t.select(); t.setSelectionRange(0, text.length);
    let ok = false; try { ok = document.execCommand('copy'); } catch (e) {}
    t.remove(); return ok;
  }
  async function post(path) { try { await fetch(q(path), { method: 'POST' }); } catch (e) {} }
  function markDone(it) { doneIds.add(it.id); saveDone(); post('/api/ack/' + it.id); }

  // ---------- aviso flutuante
  let toastTimer;
  function toast(text, sub, state) {
    clearTimeout(toastTimer);
    $('toastText').textContent = text; $('toastSub').textContent = sub || '';
    const ic = $('toastIcon'); ic.className = 'ti ' + (state || ''); ic.innerHTML = state === 'ok' ? CHECK.replace('stroke-width="3"', 'stroke-width="3.4"') : '';
    $('toast').classList.add('show');
    if (state !== 'busy') toastTimer = setTimeout(() => $('toast').classList.remove('show'), 3200);
  }

  function ringHtml(p) {
    const c = 2 * Math.PI * 14, off = c * (1 - p);
    return `<div class="ring"><svg viewBox="0 0 34 34"><circle class="bg" cx="17" cy="17" r="14" fill="none" stroke-width="3.2"/>` +
      `<circle class="fg" cx="17" cy="17" r="14" fill="none" stroke-width="3.2" stroke-linecap="round" stroke-dasharray="${c}" stroke-dashoffset="${off}"/></svg>` +
      `<span>${Math.round(p * 100)}%</span></div>`;
  }

  // ---------- lista "Do computador"
  function rowFor(it) {
    const row = document.createElement('div');
    row.className = 'row' + (firstLoad ? '' : ' new');
    const k = it.kind === 'text' ? (/^https?:\/\/\S+$/i.test(it.text.trim()) ? 'lnk' : 'txt') : kind(it.name, it.type);
    const thumb = k === 'img' && !/heic|heif/.test(it.type)
      ? `<img class="thumb" loading="lazy" src="${q('/api/file/' + it.id, '&t=1')}" alt="">`
      : `<div class="thumb ${k}">${esc(k === 'lnk' ? 'link' : k === 'txt' && it.kind === 'text' ? 'txt' : (ext(it.name) || '?').slice(0, 4))}</div>`;
    const title = it.kind === 'text' ? `<div class="txt">${esc(it.text)}</div>` : `<div class="name">${esc(it.name)}</div>`;
    row.innerHTML = `${thumb}<div class="info">${title}<div class="meta"></div></div><div class="act"></div>`;
    row._k = k;
    return row;
  }

  function paint(row, it, state, progress) {
    row.dataset.state = state;
    row.classList.toggle('done', state === 'done');
    const meta = row.querySelector('.meta'), act = row.querySelector('.act');
    const base = it.kind === 'text' ? ago(it.created) : `${fmtSize(it.size)} · ${ago(it.created)}`;
    if (state === 'busy') { meta.innerHTML = `${base} · baixando…`; act.innerHTML = ringHtml(progress || 0); return; }
    if (state === 'done') {
      const where = it.kind === 'text' ? 'copiado' : (row._k === 'img' || row._k === 'vid') ? 'salvo' : 'baixado';
      meta.innerHTML = `${base} · <span class="ok">${where}</span>`;
      act.innerHTML = `<button class="check" aria-label="Fazer de novo">${CHECK}</button>`;
      act.firstChild.onclick = () => run(it, row);
      return;
    }
    meta.textContent = base;
    const label = it.kind === 'text' ? (row._k === 'lnk' ? 'Abrir' : 'Copiar') : (row._k === 'img' || row._k === 'vid') ? 'Salvar' : 'Baixar';
    if (row._k === 'lnk') act.innerHTML = `<a class="pill" href="${esc(it.text.trim())}" target="_blank" rel="noopener">Abrir</a>`;
    else if (row._k === 'img' || row._k === 'vid') act.innerHTML = `<a class="pill" href="${q('/api/file/' + it.id)}" target="_blank" rel="noopener">Salvar</a>`;
    else act.innerHTML = `<button class="pill">${label}</button>`;
    act.firstChild.addEventListener('click', ev => {
      if (act.firstChild.tagName === 'BUTTON') ev.preventDefault();
      run(it, row, ev);
    });
  }

  function run(it, row) {
    if (it.kind === 'text') {
      if (row._k !== 'lnk' && !copy(it.text)) { toast('Não deu para copiar', 'Toque e segure o texto para copiar.'); return; }
      markDone(it); paint(row, it, 'done');
      toast(row._k === 'lnk' ? 'Link aberto' : 'Copiado', '', 'ok');
      return;
    }
    if (row._k === 'img' || row._k === 'vid') {
      // fotos e vídeos abrem para "Salvar em Fotos"
      markDone(it); paint(row, it, 'done');
      toast(row._k === 'img' ? 'Toque e segure a foto' : 'Toque em Compartilhar', row._k === 'img' ? 'e escolha Salvar em Fotos.' : 'e escolha Salvar Vídeo.', 'ok');
      return;
    }
    download(it, row);
  }

  function render(list) {
    items = list;
    const box = $('inbox');
    const ids = new Set(list.map(i => i.id));
    for (const [id, row] of rows) if (!ids.has(id)) { row.remove(); rows.delete(id); }
    if (!list.length) {
      box.innerHTML = `<div class="empty"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/></svg>Arraste arquivos para a janela do Native no PC e eles aparecem aqui.</div>`;
    } else {
      const empty = box.querySelector('.empty'); if (empty) empty.remove();
      list.forEach((it, i) => {
        let row = rows.get(it.id);
        if (!row) { row = rowFor(it); rows.set(it.id, row); paint(row, it, isDone(it) ? 'done' : 'idle'); }
        else if (row.dataset.state !== 'busy') {
          // só redesenha quando muda (baixado em outro lugar, por exemplo), para não sumir com um toque no meio
          const want = isDone(it) ? 'done' : 'idle';
          if (row.dataset.state !== want) paint(row, it, want);
        }
        if (box.children[i] !== row) box.insertBefore(row, box.children[i] || null);
      });
    }
    const pending = list.filter(it => it.kind === 'file' && !isDone(it));
    const doneCount = list.filter(isDone).length;
    $('badge').textContent = pending.length; $('badge').classList.toggle('hidden', pending.length === 0);
    $('allBtn').classList.toggle('hidden', pending.length < 2);
    $('clearBtn').classList.toggle('hidden', pending.length >= 2 || doneCount === 0);
    firstLoad = false;
  }

  // ---------- baixar (um arquivo)
  function fetchBlob(url, onProgress) {
    return new Promise((resolve, reject) => {
      const x = new XMLHttpRequest();
      x.open('GET', url); x.responseType = 'blob';
      x.onprogress = ev => { if (ev.lengthComputable) onProgress(ev.loaded / ev.total); };
      x.onload = () => x.status === 200 ? resolve(x.response) : reject(new Error(x.status));
      x.onerror = () => reject(new Error('rede'));
      x.send();
    });
  }
  function save(blob, name) {
    const url = URL.createObjectURL(new Blob([blob], { type: 'application/octet-stream' }));
    const a = document.createElement('a');
    a.href = url; a.download = name; a.style.display = 'none';
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
  }
  async function download(it, row) {
    if (row.dataset.state === 'busy') return;
    active++;
    paint(row, it, 'busy', 0);
    toast(`Baixando ${it.name}`, '', 'busy');
    try {
      const blob = await fetchBlob(q('/api/file/' + it.id, '&dl=1'), p => { row.querySelector('.act').innerHTML = ringHtml(p); });
      save(blob, it.name);
      markDone(it); paint(row, it, 'done');
      const left = items.filter(i => i.kind === 'file' && !isDone(i)).length;
      toast('Baixado', left ? `Está em Arquivos › Downloads. Faltam ${left}.` : 'Está em Arquivos › Downloads.', 'ok');
    } catch (e) {
      paint(row, it, 'idle');
      row.querySelector('.meta').innerHTML += ' · <span class="err">não deu para baixar</span>';
      toast('Não deu para baixar', 'Confira se o PC está no mesmo Wi-Fi.', '');
    } finally { active--; }
  }

  // ---------- baixar tudo: um .zip só (o iPhone não deixa um site baixar vários arquivos seguidos)
  $('allBtn').onclick = async () => {
    const pending = items.filter(it => it.kind === 'file' && !isDone(it));
    if (!pending.length) return;
    active++;
    pending.forEach(it => { const r = rows.get(it.id); if (r) paint(r, it, 'busy', 0); });
    toast(`Juntando ${pending.length} arquivos…`, '', 'busy');
    try {
      const blob = await fetchBlob(q('/api/zip', '&ids=' + pending.map(i => i.id).join(',')), p => {
        pending.forEach(it => { const r = rows.get(it.id); if (r) r.querySelector('.act').innerHTML = ringHtml(p); });
        $('toastText').textContent = `Baixando ${pending.length} arquivos · ${Math.round(p * 100)}%`;
      });
      save(blob, `Native · ${pending.length} arquivos.zip`);
      pending.forEach(it => { doneIds.add(it.id); const r = rows.get(it.id); if (r) paint(r, it, 'done'); });
      saveDone();
      toast(`${pending.length} arquivos baixados`, 'Num .zip em Arquivos › Downloads. Toque nele para abrir.', 'ok');
    } catch (e) {
      pending.forEach(it => { const r = rows.get(it.id); if (r) paint(r, it, 'idle'); });
      toast('Não deu para baixar', 'Confira se o PC está no mesmo Wi-Fi.', '');
    } finally { active--; render(items); }
  };

  $('clearBtn').onclick = () => {
    items.filter(isDone).forEach(it => { post('/api/remove/' + it.id); const r = rows.get(it.id); if (r) { r.remove(); rows.delete(it.id); } });
    render(items.filter(it => !isDone(it)));
  };

  // ---------- estado
  async function refresh() {
    if (busy || document.hidden || active) return;
    busy = true;
    try {
      const r = await fetch(q('/api/state'), { cache: 'no-store' });
      if (r.status === 401) { $('pair').classList.remove('hidden'); setStatus(false, 'Não pareado'); return; }
      const data = await r.json();
      failures = 0;
      $('pair').classList.add('hidden'); $('offline').classList.add('hidden');
      setStatus(true, data.name);
      shortcutUrl = data.shortcut || '';
      $('sc').classList.toggle('hidden', !shortcutUrl);
      render(data.items);
    } catch (e) {
      if (++failures >= 2) { $('offline').classList.remove('hidden'); setStatus(false, 'Computador fora do alcance'); }
    } finally { busy = false; }
  }
  function setStatus(ok, text) { $('dot').classList.toggle('on', ok); $('status').textContent = ok ? `Conectado a ${text}` : text; }

  // ---------- enviar
  $('picker').onchange = async e => {
    const files = [...e.target.files];
    e.target.value = '';
    if (!files.length) return;
    const list = $('uploads');
    list.classList.remove('hidden');
    const jobs = files.map(f => {
      const row = document.createElement('div');
      row.className = 'row new';
      const k = kind(f.name, f.type || '');
      row.innerHTML = `<div class="thumb ${k === 'img' ? 'gen' : k}">${esc((ext(f.name) || '?').slice(0, 4))}</div><div class="info"><div class="name">${esc(f.name)}</div>
        <div class="meta">${fmtSize(f.size)} · na fila</div></div><div class="act"></div>`;
      if (k === 'img') { const img = document.createElement('img'); img.className = 'thumb'; img.alt = ''; img.src = URL.createObjectURL(f); row.firstChild.replaceWith(img); }
      list.prepend(row);
      return { f, row };
    });
    let ok = 0;
    for (const [i, j] of jobs.entries()) {
      toast(jobs.length > 1 ? `Enviando ${i + 1} de ${jobs.length}` : `Enviando ${j.f.name}`, '', 'busy');
      if (await upload(j.f, j.row)) ok++;
    }
    toast(ok === jobs.length ? (ok > 1 ? `${ok} arquivos no computador` : 'No computador') : `${ok} de ${jobs.length} enviados`,
          ok ? 'Na pasta Downloads › Native.' : 'Confira se o PC está no mesmo Wi-Fi.', ok ? 'ok' : '');
  };

  function upload(file, row) {
    return new Promise(done => {
      const x = new XMLHttpRequest();
      const meta = row.querySelector('.meta'), act = row.querySelector('.act');
      meta.textContent = `${fmtSize(file.size)} · enviando…`; act.innerHTML = ringHtml(0);
      x.open('POST', q('/api/upload', '&name=' + encodeURIComponent(file.name)));
      x.setRequestHeader('Content-Type', file.type || 'application/octet-stream');
      x.upload.onprogress = ev => { if (ev.lengthComputable) act.innerHTML = ringHtml(ev.loaded / ev.total); };
      x.onload = () => {
        if (x.status === 200) { meta.innerHTML = `${fmtSize(file.size)} · <span class="ok">no computador</span>`; act.innerHTML = `<span class="check">${CHECK}</span>`; row.classList.add('done'); done(true); }
        else { let m = 'falhou'; try { m = JSON.parse(x.responseText).error || m; } catch (e) {} meta.innerHTML = `<span class="err">${esc(m)}</span>`; act.innerHTML = ''; done(false); }
      };
      x.onerror = () => { meta.innerHTML = '<span class="err">sem conexão com o computador</span>'; act.innerHTML = ''; done(false); };
      x.send(file);
    });
  }

  const ta = $('text'), sendBtn = $('sendText');
  ta.addEventListener('input', () => { ta.style.height = 'auto'; ta.style.height = Math.min(ta.scrollHeight, 180) + 'px'; sendBtn.disabled = !ta.value.trim(); });
  sendBtn.onclick = async () => {
    const t = ta.value.trim();
    if (!t) return;
    sendBtn.disabled = true;
    try {
      const r = await fetch(q('/api/text'), { method: 'POST', body: t, headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
      if (r.ok) { ta.value = ''; ta.style.height = 'auto'; toast('Enviado', 'Já está no Ctrl + V do computador.', 'ok'); }
      else toast('Não deu para enviar', '', '');
    } catch (e) { toast('Não deu para enviar', 'Confira se o PC está no mesmo Wi-Fi.', ''); sendBtn.disabled = false; }
  };

  // ---------- atalho
  $('scHead').onclick = () => { const h = $('scSteps').classList.toggle('hidden'); $('sc').classList.toggle('open', !h); };
  $('scCopy').onclick = () => {
    const ok = shortcutUrl && copy(shortcutUrl);
    $('scCopy').textContent = ok ? 'Copiado ✓' : 'Não deu para copiar';
    if (!ok) $('scHost').textContent = shortcutUrl;
    setTimeout(() => $('scCopy').textContent = 'Copiar endereço', 1800);
  };

  document.addEventListener('visibilitychange', refresh);
  setInterval(refresh, 2000);
  refresh();
})();
</script>
</body>
</html>
""";
}
