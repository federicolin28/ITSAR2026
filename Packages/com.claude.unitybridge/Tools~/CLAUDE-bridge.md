<!-- claude-unity-bridge -->
## Controllo dell'Editor di Unity (Claude Unity Bridge)

L'Editor di Unity aperto su questo progetto espone un server locale. Lo si comanda con `unity.bat`
nella cartella del progetto (da Bash: `./unity.bat <comando>`, da PowerShell: `.\unity.bat <comando>`).
Codici di uscita: 0 ok, 1 errore, 2 Unity non raggiungibile (Editor chiuso o in ricompilazione: riprova con `./unity.bat wait`).

Flusso di lavoro:
1. Dopo aver modificato o creato script C#: `./unity.bat refresh` — importa, attende la compilazione e stampa gli errori. Non dichiarare finito un lavoro finché `refresh` non esce con 0.
2. Per verificare il comportamento a runtime: `./unity.bat run 5` (play per 5 secondi, poi stop e riepilogo di errori/warning). Con `screenshot=1` salva anche un'immagine.
3. Per guardare la scena: `./unity.bat tree`, `./unity.bat inspect <Percorso/Oggetto> [Componente]`, `./unity.bat screenshot` (poi leggi il PNG indicato).
4. Per modificare la scena: `create`, `instantiate`, `add-component`, `set`, `delete`, poi `./unity.bat save`.
   `set` usa i nomi serializzati mostrati da `inspect` (es. `m_LocalPosition`, `speed`). Valori: numeri con il punto, vettori `1,2,3`, colori `#FF8800`, riferimenti `Assets/...` o `scena:Percorso/Oggetto`.
5. Test: `./unity.bat tests [EditMode|PlayMode] save=1`.
6. Operazioni complesse (generare livelli, configurare molti asset): scrivi una classe statica in `Assets/Editor/`, `./unity.bat refresh`, poi `./unity.bat exec NomeClasse NomeMetodo arg1 arg2`.

Regole:
- Le modifiche fatte in play mode vanno perse: esci con `./unity.bat stop` prima di modificare la scena.
- Non aprire finestre modali (dialog, "Salva con nome"): bloccano l'Editor e il bridge va in timeout.
- `./unity.bat help` elenca tutti i comandi; `./unity.bat logs problems` mostra errori e warning della Console.
<!-- /claude-unity-bridge -->
