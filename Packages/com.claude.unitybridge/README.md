# Claude Unity Bridge

Ponte gratuito tra **Claude Code** (o il browser) e l'**Editor di Unity aperto**.
Non richiede Unity AI né abbonamenti: è un pacchetto Editor che avvia un piccolo server HTTP su
`127.0.0.1` dentro Unity, più un comando `unity.bat` che Claude Code può lanciare dal terminale.

```
Claude Code ──(Bash/PowerShell)──> unity.bat ──HTTP 127.0.0.1:7777──> Editor di Unity
Browser ─────────────────────────> http://127.0.0.1:7777/ (dashboard)
```

## Installazione

```bat
install.bat "D:\Progetti\Corso ITS 2025\ITSAR2026"
```

- copia il pacchetto in `Packages\com.claude.unitybridge` (così va nel repository insieme al progetto);
- crea `unity.bat` nella radice del progetto;
- aggiunge a `CLAUDE.md` le istruzioni che dicono a Claude come usarlo.

Opzioni: `-Link` usa un riferimento `file:` a questa cartella invece di copiarla (un solo punto
da aggiornare per più progetti, ma il percorso è assoluto e vale solo su questo PC);
`-NoClaudeMd` non tocca `CLAUDE.md`.

Torna su Unity: a compilazione finita la Console mostra
`[ClaudeBridge] In ascolto su http://127.0.0.1:7777/`. Menu: **Tools > Claude Bridge**.
Con più Editor aperti ognuno prende la prima porta libera (7777, 7778, …) e la scrive in
`Library/ClaudeBridge/server.json`: `unity.bat` la legge in automatico.

## Comandi principali

| Comando | Cosa fa |
|---|---|
| `unity status` | stato: compilazione, play mode, scena attiva |
| `unity refresh` | importa i file modificati, **attende la compilazione** e stampa gli errori (exit 1 se ci sono) |
| `unity run 5` | play per 5 s, poi stop e riepilogo di errori/warning comparsi durante il gioco |
| `unity logs problems` | errori e warning della Console (`since=<id>` per i soli nuovi) |
| `unity tree` | gerarchia della scena con i componenti |
| `unity inspect Player PlayerController` | proprietà serializzate (nomi da usare con `set`) |
| `unity set Player PlayerController speed 7.5` | modifica una proprietà (con Undo) |
| `unity create Muro primitive=Cube position=0,1,5 scale=10,2,1` | crea un GameObject |
| `unity instantiate Assets/Prefabs/Enemy.prefab position=3,0,3` | istanzia un prefab |
| `unity add-component Muro Rigidbody` | aggiunge un componente |
| `unity save` | salva le scene (`unity save Assets/Scenes/Nuova.unity` per "salva con nome") |
| `unity screenshot` / `unity screenshot scene` | PNG dalla camera principale o dalla Scene View |
| `unity tests EditMode save=1` | lancia i test e riporta i fallimenti |
| `unity exec MiaClasse MioMetodo 3 "testo"` | invoca un metodo statico (anche uno script Editor scritto da Claude) |
| `unity menu "Assets/Create/Folder"` | esegue una voce di menu |
| `unity help` | elenco completo |

Codici di uscita: `0` ok, `1` errore, `2` Unity non raggiungibile.

## Come lo usa Claude Code

Basta che `CLAUDE.md` contenga la sezione aggiunta dall'installer. Un esempio di richiesta:

> Aggiungi al Player uno script che lo fa saltare con la barra spaziatrice, controlla che compili
> e prova il gioco per 5 secondi.

Claude scrive lo script, lancia `./unity.bat refresh`, corregge eventuali errori, aggiunge il
componente con `add-component`, salva e verifica con `./unity.bat run 5`.

Per operazioni che i comandi non coprono, Claude può scrivere una classe statica in `Assets/Editor/`
e invocarla con `exec`: in pratica ha accesso a tutta l'API dell'Editor.

## Note e limiti

- Il server ascolta solo su `127.0.0.1` e rifiuta le richieste senza l'header `X-Claude-Bridge`,
  così le pagine web non possono comandare Unity. Non esporlo in rete: `exec` esegue codice.
- Durante la ricompilazione il server si spegne per qualche secondo: il client lo sa e aspetta.
- Con l'Editor in secondo piano Unity rallenta i suoi aggiornamenti: i comandi funzionano ma
  possono metterci un po' di più.
- Le finestre modali (dialog di salvataggio, ecc.) bloccano il main thread: i comandi vanno in
  timeout finché non le chiudi. Per questo `open`, `save` e `tests` rifiutano i casi che le aprirebbero.
- Le modifiche fatte in play mode si perdono all'uscita, come nell'Editor.
- Testato con Unity 6000.2; richiede Unity 2022.3 o successivo.
