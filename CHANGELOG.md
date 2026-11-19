# Histórico de versões

## 1.4.2

- **Arrastar arquivos:** a janela inteira não vira mais alvo. Ao arrastar algo para o Native, ele abre a aba Arquivos e só a área "Arraste aqui" acende em azul ("Solte aqui"). Com o cursor em cima dela, a área preenche e o ícone cresce ("Solte para mandar ao iPhone"). Fora dela o cursor mostra que não dá para soltar.
- Se o arraste sai da janela sem soltar, o Native volta para a aba em que você estava.

## 1.4.1

- **Página do iPhone:** o aviso "Baixado" que ficava preso e cortado no fim da tela (no app da Tela de Início) foi removido.
- **Histórico:** tudo o que você baixa, envia ou copia aparece em "Histórico", com o nome do documento, tamanho, hora e direção (↓ baixado, ↑ enviado). Agrupado por Hoje/Ontem/data, guardado no próprio iPhone, com botão "Limpar".
- "Do computador" mostra só o que ainda falta baixar; o item baixado sai da lista com um ✓ e entra no histórico. Quando não sobra nada: "Tudo baixado".
- "Baixar tudo" (.zip) registra cada documento pelo nome no histórico.
- Erros aparecem na própria linha do arquivo.
- **Ícone:** sem o contorno branco e sem o brilho — só o celular no fundo preto. O ícone da Tela de Início do iPhone agora é um quadrado cheio (o iOS arredonda).

## 1.4.0 — 19/11/2026

- **Mini Native:** botão novo na barra de título (e na bandeja). Troca a janela grande por uma janelinha fixa por cima de tudo, com o iPhone (conexão, bateria, carregando), o que está tocando com controles e as 4 últimas notificações. Clicar numa notificação abre o Native completo; arrastar arquivos para o mini manda para o iPhone. Ele lembra onde ficou e abre assim da próxima vez.
- **Native no iPhone, redesenhado:**
  - Cada arquivo mostra o estado: botão **Baixar**, anel de progresso enquanto baixa e um **✓ verde "baixado"** quando termina. O iPhone lembra o que já foi baixado, mesmo fechando o app.
  - **Baixar tudo**: junta os arquivos pendentes num .zip só (o iOS não deixa um site baixar vários arquivos seguidos).
  - Aviso flutuante embaixo ("Baixado · Faltam 3", "3 arquivos no computador"), contador de pendentes e **Limpar baixados**.
  - Ícones coloridos por tipo (PDF, Word, Excel, PowerPoint, vídeo...), miniatura das fotos e progresso também no envio para o PC.

## 1.3.3 — 19/11/2026

- **Corrigido:** aparecia um contorno escuro ("fantasma") em volta do banner de notificação. O Windows 11 estava pondo moldura, borda e sombra na janela transparente inteira; agora o banner e o mini player desenham só o próprio cartão.

## 1.3.2 — 17/11/2026

- **Carregando:** quando o iPhone está na tomada, a bateria fica verde com um raio, como no iPhone. Aviso quando chega a 100%. O iPhone não informa isso por Bluetooth, então o Native percebe pela porcentagem subindo (leva de 1 a 3 minutos para aparecer).

## 1.3.1 — 17/11/2026

- **Barra de título no padrão do Windows:** minimizar, maximizar e fechar no canto direito. À esquerda ficam Não Perturbe, Ajustes (engrenagem) e Fixar.
- **Fixar na frente:** o alfinete deixa a janela por cima de tudo e ela não some ao clicar em outro programa. A escolha fica salva e também está no menu da bandeja.
- **Ajustes saiu das abas** e virou a engrenagem; clicar de novo volta para a aba anterior. As abas voltaram a ser Notificações, Mensagens, Telefone e Arquivos (Ctrl+1…4; Ctrl+, abre os Ajustes).
- **Corrigido:** no iPhone, "Baixar" abria o arquivo em vez de baixar. Agora o arquivo é baixado com barra de progresso e vai para Arquivos › Downloads.

## 1.3.0 — 17/11/2026

- **Novo nome: Native** (antes "Continuidade"). Os ajustes, a agenda guardada, os registros e o pareamento do iPhone são trazidos automaticamente; o instalador remove a versão antiga, o atalho antigo do "Enviar para" e a regra antiga do firewall.
- **Nova logo:** iPhone em metal polido sobre fundo preto.
- Arquivos recebidos do iPhone agora vão para Downloads › Native (os antigos continuam em Downloads › Continuidade).
- O nome do PC na rede para o Atalho do iPhone passou a ser `native-<nome-do-pc>.local`.

## 1.2.0 — 17/11/2026

- **Arquivos, o "AirDrop" do Native:** nova aba Arquivos (Ctrl+4).
  - PC → iPhone: arrastar para qualquer lugar da janela, Escolher arquivos, Ctrl+V (arquivos, print ou texto), botão direito › Enviar para › iPhone (Native), ou pela bandeja. Pastas viram .zip.
  - iPhone → PC: página do Native no iPhone (vira app na Tela de Início) com fotos, vídeos e arquivos, com barra de progresso; ou o Atalho "Enviar para o PC" no menu Compartilhar. Tudo cai em Downloads › Native, com banner.
  - Textos e links do iPhone vão direto para a área de transferência do PC.
  - Pareamento por QR code com chave secreta; só aceita conexões da rede local. Nome `.local` próprio (mDNS) para o Atalho não quebrar quando o IP muda.
  - Instalador libera o app no firewall (só redes privadas) e cria o "Enviar para".
- As abas agora são Avisos, Mensagens, Telefone, Arquivos e Ajustes (Ctrl+1…5).
- 4 testes novos (QR code, mDNS, nomes de arquivo e o servidor completo em localhost): 36 no total.

## 1.1.0 — 17/11/2026

- **Player no estilo do iOS:** título, artista, barra com tempos, controles grandes e barrinhas animadas enquanto toca.
- **Volume do iPhone:** barra de volume, botões − e + e rolagem do mouse sobre o player.
- **Arrastar para pular:** a barra de progresso pode ser arrastada nos apps que aceitam avançar/voltar (podcasts, audiolivros, YouTube…). Botões de ±15 s aparecem quando o app permite.
- **Mini player flutuante (PiP):** fica por cima de todas as janelas, arrasta para qualquer canto e lembra a posição. Abre pelo botão do player ou pela bandeja.
- **Corrigido:** o X de limpar notificação aparecia cortado.

## 1.0.0 — 17/11/2026

Primeira versão completa.

- **Mensagens:** conversas de SMS e iMessage, histórico em balões, nova mensagem e resposta pelo banner (MAP).
- **Telefone:** ligações recentes, agenda com fotos e discagem pelo iPhone (PBAP e HFP).
- **Fotos dos contatos** nas notificações, conversas e ligações.
- **Guia de conexão** com passo a passo e o passo atual destacado.
- **Tema claro e escuro**, seguindo o Windows ou fixo.
- **Reconexão automática** ao voltar da suspensão, desbloquear a sessão ou religar o Bluetooth, com verificação periódica.
- **Instalador** sem administrador, atalho no Menu Iniciar e desinstalador em "Aplicativos instalados".
- **Ícone próprio**, menu da bandeja com estado do iPhone e atalhos Ctrl+1…4.
- **Registro de funcionamento** (sem conteúdo pessoal) e "Copiar diagnóstico" nos Ajustes.
- Abrir o app de novo traz a janela que já está aberta para a frente.
- Nome dos apps vem do próprio iPhone ("WhatsApp", e não "WhatsApp Messenger").
- 29 testes automáticos de protocolo, incluindo um iPhone simulado.

## 0.2.0

- Tocando Agora, códigos de verificação, Não Perturbe, som, atalho global, fila de banners, menu de contexto, aviso de bateria, modelo do iPhone, receptor AirPlay sempre ligado.

## 0.1.0

- Central de notificações via ANCS, banners e espelhamento via AirPlay.
