# LiteDeck

Substituto leve do app de controle do fabricante para notebooks com **chassi Uniwill**. Um único `.exe` de ~250 KB que fica na bandeja do sistema e conversa direto com o EC (Embedded Controller) pelo driver do fabricante.

## O que faz

- **Teclado RGB**: cor e brilho (níveis 0–4, incluindo apagado).
- **Ventoinhas**: perfis (padrão, silencioso, turbo, fixo) e Fan Boost.
- **Sensores**: temperatura de CPU/GPU e rotação das ventoinhas.
- **Opções gerais**: Wi-Fi, Bluetooth, webcam, touchpad, bloqueio de teclas e taxa de atualização da tela.
- Inicia com o Windows já minimizado na bandeja, sem pedir UAC (via Agendador de Tarefas).

## Requisitos

- Windows 10/11 x64.
- Notebook com **chassi Uniwill** e o driver **UWACPIDriver** instalado (ele vem junto com o app do fabricante; o LiteDeck usa o dispositivo `\\.\ACPIDriver`). Depois de instalar o driver, o app do fabricante pode ser desativado ou removido, desde que o driver continue presente.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).
- Permissão de administrador (o acesso ao EC exige).

## Instalação

Baixe/gere a pasta `dist` e, no PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

O script pede UAC e então:

1. copia `LiteDeck.exe` para `%ProgramFiles%\LiteDeck`;
2. cria o atalho **LiteDeck** no Menu Iniciar;
3. registra a tarefa agendada `LiteDeck` (ao fazer logon, privilégio máximo, argumento `--tray`), liberada para rodar na bateria;
4. abre o LiteDeck na bandeja.

Rodar o `install.ps1` de novo atualiza a instalação (o app em execução é encerrado e substituído).

### Desinstalação

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

Encerra o app, remove a tarefa agendada, o atalho e a pasta de instalação. As configurações em `%AppData%\LiteDeck\settings.json` são mantidas; para apagá-las também, use `-Purge`:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -Purge
```

## Compilar

Requer o .NET 10 SDK.

```powershell
.\publish.ps1
```

Gera `dist\LiteDeck.exe` (Release, win-x64, arquivo único, dependente do runtime) e copia os scripts de instalação para `dist`.

## Linha de comando

| Argumento | Efeito |
|---|---|
| *(nenhum)* | Abre a janela principal. |
| `--tray` | Inicia só com o ícone na bandeja. |
| `--selftest arquivo.txt` | Autoteste de hardware (ver abaixo). |

### Autoteste

```powershell
& "$env:ProgramFiles\LiteDeck\LiteDeck.exe" --selftest "$env:USERPROFILE\Desktop\selftest.txt"
```

Exercita cada função no EC (sensores, níveis de brilho, cor do teclado, ventoinhas...), lê de volta para conferir, restaura o estado original e grava o resultado linha a linha (`[OK  ]` / `[FALHA]`) no arquivo indicado. Útil para verificar se o seu modelo é compatível antes de usar o app no dia a dia. Rode com o LiteDeck fechado na bandeja para os dois não disputarem o EC.

## ⚠️ Aviso

O LiteDeck **escreve diretamente em registradores do EC** do notebook. Os endereços foram mapeados em um modelo específico de chassi Uniwill e podem ter outro significado em modelos diferentes. Valores errados podem desligar ventoinhas, causar superaquecimento ou deixar o EC em estado inconsistente até um desligamento completo.

**Use por sua conta e risco.** Este projeto não tem relação com o fabricante do notebook nem com a Uniwill, e é fornecido sem garantia de qualquer tipo.
