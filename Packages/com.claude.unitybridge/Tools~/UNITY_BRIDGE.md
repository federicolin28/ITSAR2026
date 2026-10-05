# Controllo dell'Editor di Unity da terminale (Claude Unity Bridge)

Istruzioni per gli assistenti AI che possono eseguire comandi nel terminale (Claude Code, Copilot in
modalità agent, Gemini CLI…). Chi usa l'AI in chat può lanciare gli stessi comandi a mano e incollare
l'output nella conversazione.

L'Editor di Unity aperto su questo progetto espone un server locale (pacchetto
`Packages/com.claude.unitybridge`). Lo si comanda con `unity.bat` nella radice del progetto:
da Bash `./unity.bat <comando>`, da PowerShell `.\unity.bat <comando>`.
Codici di uscita: 0 ok, 1 errore, 2 Unity non raggiungibile (Editor chiuso o in ricompilazione:
riprova dopo `./unity.bat wait`).

## Flusso di lavoro

1. Dopo aver creato o modificato script C#: `./unity.bat refresh`. Importa i file, attende la
   compilazione e stampa gli errori. Un lavoro non è finito finché `refresh` non esce con 0.
2. Per verificare il comportamento a runtime: `./unity.bat run 5` (play per 5 secondi, poi stop e
   riepilogo di errori e warning). Con `screenshot=1` salva anche un'immagine del gioco.
3. Per guardare la scena: `./unity.bat tree`, `./unity.bat inspect <Percorso/Oggetto> [Componente]`,
   `./unity.bat screenshot` (poi apri il PNG indicato). I percorsi sono quelli della Hierarchy,
   per esempio `Gameplay/Player`.
4. Per modificare la scena: `create`, `instantiate`, `add-component`, `set`, `delete`, poi
   `./unity.bat save`. `set` usa i nomi serializzati mostrati da `inspect` (es. `m_LocalPosition`,
   `speed`). Valori: numeri con il punto, vettori `1,2,3`, colori `#FF8800`, riferimenti `Assets/...`
   oppure `scena:Percorso/Oggetto`.
5. Test: `./unity.bat tests EditMode save=1`.
6. Operazioni complesse (generare livelli, configurare molti asset): scrivi una classe statica in
   `Assets/Editor/`, lancia `./unity.bat refresh`, poi `./unity.bat exec NomeClasse NomeMetodo arg1 arg2`.

## Regole

- Le modifiche fatte in play mode si perdono: esci con `./unity.bat stop` prima di modificare la scena.
- Non aprire finestre modali (dialog, "Salva con nome"): bloccano l'Editor e i comandi vanno in timeout.
- `./unity.bat help` elenca tutti i comandi; `./unity.bat logs problems` mostra errori e warning della Console.
