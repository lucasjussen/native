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
  --bg:#f2f2f7; --card:#fff; --text:#000; --sec:#6c6c70; --ter:#aeaeb2; --sep:rgba(60,60,67,.18);
  --blue:#007aff; --green:#34c759; --red:#ff3b30; --fill:rgba(120,120,128,.12);
}
@media (prefers-color-scheme: dark){
  :root{ --bg:#000; --card:#1c1c1e; --text:#fff; --sec:#98989f; --ter:#636366; --sep:rgba(84,84,88,.6);
         --blue:#0a84ff; --green:#30d158; --red:#ff453a; --fill:rgba(120,120,128,.24); }
}
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
html,body{margin:0;background:var(--bg);color:var(--text);
  font:17px/1.35 -apple-system,BlinkMacSystemFont,"SF Pro Text",system-ui,sans-serif;-webkit-font-smoothing:antialiased}
body{padding:calc(env(safe-area-inset-top) + 10px) 16px calc(env(safe-area-inset-bottom) + 28px)}
h1{font-size:34px;font-weight:700;letter-spacing:.37px;margin:8px 4px 2px}
.sub{color:var(--sec);font-size:15px;margin:0 4px 18px;display:flex;align-items:center;gap:7px}
.dot{width:8px;height:8px;border-radius:50%;background:var(--ter);flex:none}
.dot.on{background:var(--green)}
h2{font-size:13px;font-weight:400;text-transform:uppercase;color:var(--sec);margin:26px 16px 7px;letter-spacing:-.08px}
.card{background:var(--card);border-radius:12px;overflow:hidden}
.row{display:flex;align-items:center;gap:12px;padding:11px 14px;min-height:60px;position:relative}
.row+.row::before{content:"";position:absolute;top:0;left:74px;right:0;border-top:.5px solid var(--sep)}
.thumb{width:46px;height:46px;border-radius:8px;background:var(--fill);flex:none;object-fit:cover;
  display:flex;align-items:center;justify-content:center;font-size:12px;font-weight:600;color:var(--sec);text-transform:uppercase}
.info{flex:1;min-width:0}
.name{white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.meta{color:var(--sec);font-size:13px;margin-top:1px}
.txt{white-space:pre-wrap;word-break:break-word;font-size:15px;max-height:7.5em;overflow:hidden}
.btn{appearance:none;border:0;border-radius:980px;background:var(--fill);color:var(--blue);font:600 15px -apple-system,system-ui;
  padding:7px 14px;flex:none;text-decoration:none;display:inline-block}
.btn.primary{background:var(--blue);color:#fff}
.x{appearance:none;border:0;background:none;color:var(--ter);font-size:20px;padding:4px 2px 4px 6px;flex:none;line-height:1}
.empty{color:var(--sec);text-align:center;padding:26px 16px;font-size:15px}
.big{display:flex;flex-direction:column;align-items:center;gap:6px;width:100%;padding:22px 16px;border:0;
  background:var(--card);border-radius:12px;color:var(--blue);font:600 17px -apple-system,system-ui}
.big svg{width:34px;height:34px}
.big small{color:var(--sec);font-weight:400;font-size:13px}
textarea{width:100%;border:0;resize:none;background:transparent;color:var(--text);font:17px -apple-system,system-ui;
  padding:12px 14px;min-height:46px;outline:none}
.send-row{display:flex;justify-content:flex-end;padding:0 10px 10px}
.bar{height:4px;border-radius:2px;background:var(--fill);margin-top:7px;overflow:hidden}
.bar i{display:block;height:100%;width:0;background:var(--blue);transition:width .15s}
.ok{color:var(--green)} .err{color:var(--red)}
.tip{background:var(--card);border-radius:12px;padding:13px 14px;font-size:15px;color:var(--sec);margin-top:14px;display:flex;gap:10px}
.tip b{color:var(--text);font-weight:600}
.tip .x{margin-left:auto;align-self:flex-start}
.banner{border-radius:12px;padding:13px 14px;font-size:15px;margin-top:6px;background:var(--card);color:var(--red)}
.hidden{display:none !important}
.sc-steps{padding:2px 16px 14px;font-size:15px}
.sc-steps ol{padding-left:20px;margin:6px 0 12px;line-height:1.5}
.sc-copy{display:flex;gap:8px;align-items:center;flex-wrap:wrap}
.sc-copy .meta{word-break:break-all}
.foot{color:var(--ter);font-size:12px;text-align:center;margin-top:28px;padding:0 20px}
@keyframes pop{from{transform:scale(.97);opacity:.4}to{transform:none;opacity:1}}
.new{animation:pop .35s ease-out}
</style>
</head>
<body>
<h1>Native</h1>
<div class="sub"><span class="dot" id="dot"></span><span id="status">Conectando…</span></div>

<div id="pair" class="banner hidden">Este endereço não está pareado. No computador, abra o Native → <b>Arquivos</b> e escaneie o QR code de novo com a Câmera.</div>
<div id="offline" class="banner hidden">Não encontrei o computador. Confira se o iPhone está no mesmo Wi-Fi e se o Native está aberto no PC.</div>

<div id="hint" class="tip hidden">
  <div>Para ter isso como um app: toque em <b>Compartilhar</b> <span aria-hidden="true">⬆︎</span> e depois em <b>Adicionar à Tela de Início</b>.</div>
  <button class="x" id="hintClose" aria-label="Fechar">×</button>
</div>

<h2>Do computador</h2>
<div class="card" id="inbox"><div class="empty">Arraste arquivos para o Native no PC e eles aparecem aqui.</div></div>

<h2>Área de transferência</h2>
<div class="card" id="clipCard">
  <div class="row"><div class="info"><div class="name">O que está copiado no PC</div>
    <div class="meta" id="clipMeta">Toque para trazer para cá.</div></div>
    <button class="btn" id="clipGet">Ver</button></div>
  <div id="clipBox" class="hidden" style="padding:0 14px 14px">
    <div class="txt" id="clipText" style="background:var(--fill);border-radius:10px;padding:10px 12px;max-height:12em;overflow:auto;-webkit-user-select:text;user-select:text"></div>
    <img id="clipImg" class="hidden" alt="Imagem copiada no PC" style="max-width:100%;border-radius:10px;display:block">
    <div style="display:flex;justify-content:flex-end;margin-top:10px"><button class="btn primary" id="clipCopy">Copiar no iPhone</button></div>
  </div>
</div>

<h2>Enviar para o computador</h2>
<label class="big" for="picker">
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 16V4M7 9l5-5 5 5"/><path d="M5 14v4a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-4"/></svg>
  Fotos, vídeos ou arquivos
  <small>Vão para a pasta Downloads › Native</small>
</label>
<input type="file" id="picker" multiple class="hidden">
<div class="card hidden" id="uploads" style="margin-top:10px"></div>

<div class="card" style="margin-top:10px">
  <textarea id="text" rows="1" placeholder="Texto ou link para o computador"></textarea>
  <div class="send-row"><button class="btn primary" id="sendText">Enviar</button></div>
</div>

<h2>Atalhos do iPhone</h2>
<div class="card" id="sc">
  <div class="row sc-head" data-k="up" style="cursor:pointer"><div class="info"><div class="name">“Enviar para o PC”</div>
    <div class="meta">Compartilhar → Enviar para o PC, como o AirDrop.</div></div><span class="meta sc-arrow">›</span></div>
  <div class="sc-steps hidden" data-k="up">
    <ol>
      <li>Toque em <b>Copiar endereço</b> abaixo.</li>
      <li>App <b>Atalhos</b> → <b>+</b>. Nome: <b>Enviar para o PC</b>.</li>
      <li>No bloco <b>Receber</b>, troque <b>Lugar Nenhum</b> por <b>Planilha de Compartilhamento</b>.</li>
      <li>Adicione <b>Repetir com Cada</b> → <b>Entrada do Atalho</b>.</li>
      <li>Dentro dele, <b>Obter Conteúdo do URL</b>: cole o endereço; Método <b>POST</b>; Cabeçalho <b>X-Filename</b> = <b>Item da Repetição › Nome</b>; Corpo <b>Arquivo</b> = <b>Item da Repetição</b>.</li>
    </ol>
    <div class="sc-copy"><button class="btn primary" data-copy="up">Copiar endereço</button><span class="meta"></span></div>
  </div>

  <div class="row sc-head" data-k="send" style="cursor:pointer"><div class="info"><div class="name">“Copiar para o PC”</div>
    <div class="meta">Copiou no iPhone, cola no PC. Dá para ligar ao toque nas costas.</div></div><span class="meta sc-arrow">›</span></div>
  <div class="sc-steps hidden" data-k="send">
    <ol>
      <li>Toque em <b>Copiar endereço</b> abaixo.</li>
      <li>App <b>Atalhos</b> → <b>+</b>. Nome: <b>Copiar para o PC</b>.</li>
      <li>Adicione <b>Obter Área de Transferência</b>.</li>
      <li>Adicione <b>Obter Conteúdo do URL</b>: cole o endereço; Método <b>POST</b>; Corpo <b>Arquivo</b> = <b>Área de Transferência</b>.</li>
      <li>Toque nas costas: <b>Ajustes</b> → <b>Acessibilidade</b> → <b>Toque</b> → <b>Tocar Atrás</b> → <b>Toque Duplo</b> → <b>Copiar para o PC</b>.</li>
    </ol>
    <div class="sc-copy"><button class="btn primary" data-copy="clip">Copiar endereço</button><span class="meta"></span></div>
  </div>

  <div class="row sc-head" data-k="get" style="cursor:pointer"><div class="info"><div class="name">“Pegar do PC”</div>
    <div class="meta">Copiou no PC, cola no iPhone.</div></div><span class="meta sc-arrow">›</span></div>
  <div class="sc-steps hidden" data-k="get">
    <ol>
      <li>Toque em <b>Copiar endereço</b> abaixo (é o mesmo do anterior).</li>
      <li>App <b>Atalhos</b> → <b>+</b>. Nome: <b>Pegar do PC</b>.</li>
      <li>Adicione <b>Obter Conteúdo do URL</b> e cole o endereço (Método <b>GET</b>, o padrão).</li>
      <li>Adicione <b>Copiar para Área de Transferência</b>.</li>
      <li>Toque nas costas: em <b>Tocar Atrás</b>, use <b>Toque Triplo</b> → <b>Pegar do PC</b>.</li>
    </ol>
    <div class="sc-copy"><button class="btn primary" data-copy="clip">Copiar endereço</button><span class="meta"></span></div>
  </div>
</div>

<p class="foot">Mantenha esta tela aberta para ver na hora o que o PC mandar.<br>Funciona só no mesmo Wi-Fi do computador.</p>

<script>
(() => {
  const key = new URLSearchParams(location.search).get('k') || '';
  const $ = id => document.getElementById(id);
  const q = (p, extra) => `${p}?k=${encodeURIComponent(key)}${extra || ''}`;
  let known = new Set(), first = true, failures = 0, busy = false, shortcutUrl = '', downloading = 0;

  let clipUrl = '';
  document.querySelectorAll('.sc-head').forEach(head => head.onclick = () => {
    const steps = document.querySelector(`.sc-steps[data-k="${head.dataset.k}"]`);
    const hidden = steps.classList.toggle('hidden');
    head.querySelector('.sc-arrow').textContent = hidden ? '›' : '⌄';
  });
  document.querySelectorAll('[data-copy]').forEach(b => b.onclick = () => {
    const url = b.dataset.copy === 'up' ? shortcutUrl : clipUrl;
    const ok = url && copy(url);
    b.textContent = ok ? 'Copiado ✓' : 'Não deu para copiar';
    if (!ok) b.nextElementSibling.textContent = url;
    setTimeout(() => b.textContent = 'Copiar endereço', 1800);
  });

  // ---------- área de transferência do PC
  let clipValue = '';
  $('clipGet').onclick = async () => {
    const b = $('clipGet');
    b.textContent = '…';
    try {
      const r = await fetch(q('/api/clip'), { cache: 'no-store' });
      const box = $('clipBox'), txt = $('clipText'), img = $('clipImg');
      if (r.status === 204) { $('clipMeta').textContent = 'Nada copiado no PC agora.'; box.classList.add('hidden'); }
      else if (r.status === 403) { $('clipMeta').textContent = 'Desligado nos Ajustes do Native no PC.'; box.classList.add('hidden'); }
      else if (r.ok) {
        const type = r.headers.get('Content-Type') || '';
        box.classList.remove('hidden');
        if (type.startsWith('image/')) {
          const blob = await r.blob();
          img.src = URL.createObjectURL(blob); img.classList.remove('hidden'); txt.classList.add('hidden');
          $('clipCopy').classList.add('hidden');
          $('clipMeta').textContent = 'Imagem: toque e segure para copiar ou salvar.';
        } else {
          clipValue = await r.text();
          txt.textContent = clipValue; txt.classList.remove('hidden'); img.classList.add('hidden');
          $('clipCopy').classList.remove('hidden');
          $('clipMeta').textContent = 'Copiado no PC agora há pouco.';
        }
      }
    } catch (e) { $('clipMeta').textContent = 'Sem conexão com o computador.'; }
    b.textContent = 'Atualizar';
  };
  $('clipCopy').onclick = () => {
    const ok = clipValue && copy(clipValue);
    $('clipCopy').textContent = ok ? 'Copiado ✓' : 'Selecione o texto acima';
    setTimeout(() => $('clipCopy').textContent = 'Copiar no iPhone', 1800);
  };

  if (!navigator.standalone && !localStorage.getItem('hintClosed')) $('hint').classList.remove('hidden');
  $('hintClose').onclick = () => { $('hint').classList.add('hidden'); try { localStorage.setItem('hintClosed', '1'); } catch (e) {} };

  const fmtSize = n => n < 1024 ? n + ' B' : n < 1048576 ? (n / 1024).toFixed(0) + ' KB'
    : n < 1073741824 ? (n / 1048576).toFixed(1).replace('.', ',') + ' MB' : (n / 1073741824).toFixed(2).replace('.', ',') + ' GB';
  const ago = ms => { const s = Math.max(0, (Date.now() - ms) / 1000);
    return s < 60 ? 'agora' : s < 3600 ? `há ${Math.floor(s / 60)} min` : s < 86400 ? `há ${Math.floor(s / 3600)} h` : new Date(ms).toLocaleDateString('pt-BR'); };
  const ext = n => (n.split('.').pop() || '').slice(0, 4);
  const esc = s => s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  function setStatus(ok, text) { $('dot').classList.toggle('on', ok); $('status').textContent = text; }

  function copy(text) {
    const t = document.createElement('textarea');
    t.value = text; t.setAttribute('readonly', ''); t.style.position = 'fixed'; t.style.opacity = '0';
    document.body.appendChild(t); t.select(); t.setSelectionRange(0, text.length);
    let ok = false; try { ok = document.execCommand('copy'); } catch (e) {}
    t.remove(); return ok;
  }

  async function post(path) { try { await fetch(q(path), { method: 'POST' }); } catch (e) {} }

  function render(items) {
    const box = $('inbox');
    if (!items.length) { box.innerHTML = '<div class="empty">Arraste arquivos para o Native no PC e eles aparecem aqui.</div>'; return; }
    box.innerHTML = '';
    for (const it of items) {
      const row = document.createElement('div');
      row.className = 'row' + (!first && !known.has(it.id) ? ' new' : '');
      const when = ago(it.created) + (it.delivered ? ' · salvo' : '');
      if (it.kind === 'text') {
        const isLink = /^https?:\/\/\S+$/i.test(it.text.trim());
        row.innerHTML = `<div class="thumb">${isLink ? 'link' : 'txt'}</div>
          <div class="info"><div class="txt">${esc(it.text)}</div><div class="meta">${when}</div></div>
          ${isLink ? `<a class="btn" href="${esc(it.text.trim())}" target="_blank" rel="noopener">Abrir</a>` : `<button class="btn">Copiar</button>`}
          <button class="x" aria-label="Remover">×</button>`;
        const b = row.querySelector('button.btn');
        if (b) b.onclick = () => { b.textContent = copy(it.text) ? 'Copiado' : 'Selecione'; post('/api/ack/' + it.id); };
        else row.querySelector('a.btn').addEventListener('click', () => post('/api/ack/' + it.id));
      } else {
        const img = it.type.startsWith('image/') && !/heic|heif/.test(it.type);
        const media = img || it.type.startsWith('video/');
        const thumb = img ? `<img class="thumb" loading="lazy" src="${q('/api/file/' + it.id, '&t=1')}" alt="">` : `<div class="thumb">${esc(ext(it.name))}</div>`;
        // Fotos e vídeos abrem para "Salvar em Fotos"; o resto vai para o app Arquivos.
        const href = media ? q('/api/file/' + it.id) : q('/api/file/' + it.id, '&dl=1');
        row.innerHTML = `${thumb}<div class="info"><div class="name">${esc(it.name)}</div><div class="meta">${fmtSize(it.size)} · ${when}</div></div>
          <a class="btn" href="${href}"${media ? ' target="_blank" rel="noopener"' : ''}>${media ? 'Abrir' : 'Baixar'}</a>
          <button class="x" aria-label="Remover">×</button>`;
        const link = row.querySelector('a.btn');
        if (media) link.addEventListener('click', () => post('/api/ack/' + it.id));
        else link.addEventListener('click', ev => { ev.preventDefault(); download(it, link, row); });
      }
      row.querySelector('.x').onclick = () => { row.remove(); post('/api/remove/' + it.id); };
      box.appendChild(row);
    }
    known = new Set(items.map(i => i.id));
    first = false;
  }

  async function refresh() {
    if (busy || document.hidden) return;
    busy = true;
    try {
      const r = await fetch(q('/api/state'), { cache: 'no-store' });
      if (r.status === 401) { $('pair').classList.remove('hidden'); setStatus(false, 'Não pareado'); return; }
      const data = await r.json();
      failures = 0;
      $('pair').classList.add('hidden'); $('offline').classList.add('hidden');
      setStatus(true, data.name);
      shortcutUrl = data.shortcut || '';
      clipUrl = data.clipUrl || '';
      $('sc').classList.toggle('hidden', !shortcutUrl);
      if (!downloading) render(data.items);   // não redesenha a lista no meio de um download
    } catch (e) {
      if (++failures >= 2) { $('offline').classList.remove('hidden'); setStatus(false, 'Computador fora do alcance'); }
    } finally { busy = false; }
  }

  // ---------- baixar no iPhone
  // Baixa o arquivo inteiro aqui e entrega ao iPhone como download (vai para o app Arquivos),
  // em vez de abrir a visualização do Safari.
  function download(it, link, row) {
    if (link.dataset.busy) return;
    link.dataset.busy = '1';
    downloading++;
    const meta = row.querySelector('.meta');
    const before = meta.innerHTML;
    const x = new XMLHttpRequest();
    x.open('GET', q('/api/file/' + it.id, '&dl=1'));
    x.responseType = 'blob';
    x.onprogress = ev => { if (ev.lengthComputable) link.textContent = Math.round(ev.loaded / ev.total * 100) + '%'; };
    x.onload = () => {
      delete link.dataset.busy; downloading--;
      if (x.status !== 200) { link.textContent = 'Baixar'; meta.innerHTML = '<span class="err">não deu para baixar</span>'; return; }
      const blob = new Blob([x.response], { type: 'application/octet-stream' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url; a.download = it.name; a.style.display = 'none';
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 60000);
      link.textContent = 'Baixar';
      meta.innerHTML = before + ' · <span class="ok">em Arquivos › Downloads</span>';
      setTimeout(() => { if (!downloading) refresh(); }, 2500);
      post('/api/ack/' + it.id);
    };
    x.onerror = () => { delete link.dataset.busy; downloading--; link.textContent = 'Baixar'; meta.innerHTML = '<span class="err">sem conexão com o computador</span>'; };
    x.send();
  }

  // ---------- envio
  $('picker').onchange = async e => {
    const files = [...e.target.files];
    e.target.value = '';
    if (!files.length) return;
    const list = $('uploads');
    list.classList.remove('hidden');
    for (const f of files) {
      const row = document.createElement('div');
      row.className = 'row';
      row.innerHTML = `<div class="thumb">${esc(ext(f.name) || '?')}</div><div class="info"><div class="name">${esc(f.name)}</div>
        <div class="meta">${fmtSize(f.size)}</div><div class="bar"><i></i></div></div>`;
      list.prepend(row);
      f._row = row;
    }
    for (const f of files) await upload(f, f._row);
  };

  function upload(file, row) {
    return new Promise(done => {
      const x = new XMLHttpRequest();
      const bar = row.querySelector('.bar i'), meta = row.querySelector('.meta');
      x.open('POST', q('/api/upload', '&name=' + encodeURIComponent(file.name)));
      x.setRequestHeader('Content-Type', file.type || 'application/octet-stream');
      x.upload.onprogress = ev => { if (ev.lengthComputable) bar.style.width = (ev.loaded / ev.total * 100).toFixed(1) + '%'; };
      x.onload = () => {
        row.querySelector('.bar').remove();
        if (x.status === 200) { meta.innerHTML = `${fmtSize(file.size)} · <span class="ok">no computador ✓</span>`; }
        else { let m = 'falhou'; try { m = JSON.parse(x.responseText).error || m; } catch (e) {} meta.innerHTML = `<span class="err">${esc(m)}</span>`; }
        done();
      };
      x.onerror = () => { meta.innerHTML = '<span class="err">sem conexão com o computador</span>'; row.querySelector('.bar')?.remove(); done(); };
      x.send(file);
    });
  }

  const ta = $('text');
  ta.addEventListener('input', () => { ta.style.height = 'auto'; ta.style.height = Math.min(ta.scrollHeight, 180) + 'px'; });
  $('sendText').onclick = async () => {
    const t = ta.value.trim();
    if (!t) return;
    const b = $('sendText');
    b.disabled = true;
    try {
      const r = await fetch(q('/api/text'), { method: 'POST', body: t, headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
      if (r.ok) { ta.value = ''; ta.style.height = 'auto'; b.textContent = 'Enviado ✓'; setTimeout(() => b.textContent = 'Enviar', 1500); }
    } catch (e) { b.textContent = 'Falhou'; setTimeout(() => b.textContent = 'Enviar', 1500); }
    b.disabled = false;
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
