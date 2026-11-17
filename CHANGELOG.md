# Histórico de versões

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
