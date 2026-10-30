# Deep Space AI: Probably DeepSeek

A native Probably Stolen mod featuring Xiaojing, a pixel-art whale-girl maid assistant. Chinese and English UI, built-in prompts and tool descriptions follow the game's language setting automatically. Other languages use English. Your custom prompt and your own text are preserved.

GitHub: [https://github.com/Mooooooon/ProbablyAI](https://github.com/Mooooooon/ProbablyAI) | Steam Workshop: [https://steamcommunity.com/sharedfiles/filedetails/?id=3830081737](https://steamcommunity.com/sharedfiles/filedetails/?id=3830081737)

## Getting started

1. Subscribe in Steam Workshop, enable the mod in the game's Mods menu, review its **network** capability and restart the game.
2. Enter your pawnshop. Click Xiaojing's portrait to chat. **F6** toggles the portrait; **F7** opens assistant settings.
3. Enter an OpenAI-compatible base URL, API key and model. Defaults: `https://api.deepseek.com`, `deepseek-flash`. You can fetch the provider's model list or enter a model ID manually.
4. View or edit the full system prompt in F7. The default maid persona addresses you as **Master**, speaks warmly, and avoids emoji and emoticons unless requested. Save settings to apply changes to future messages, or preview the current draft with a test message.

You need your own compatible API service and credentials. Provider charges are handled by your chosen provider. The mod contains no shared API key or included API credits.

The chat window remembers its dragged position when closed and reopened, including after restarting the game. Its position is saved separately in local `ModData/probably_ai/chat-window.local.json`; this file contains no chat or API key and is excluded from uploads.

## Things to try

- "Put the food in my inventory on the counter."
- "What does the current customer want, and how much budget do they have left?"
- "Find items this customer would buy and can afford. Tell me first."
- "Move the note in Archives → Mentor Notes to the counter."
- "Put the scrap metal into the furnace's input compartment."

The assistant queries live containers and items, including nested folders, bags, storage pages and machine compartments. It can inspect items, move them through the game's native rules and open or close container windows. Customer queries include buying preferences, remaining purchase budgets and barter, procurement and gunsmith requirements. Item checks compare native buying rules with the current budget estimate.

The mod does not complete purchases or sales, choose dialogue options, unlock containers, spawn items or start machines. Native game callbacks and automatic processing still behave normally. Moves can partially succeed. Stopping a request cancels later actions; completed moves remain. The model must support Chat Completions tool calling to use game tools.

## Language and prompts

The mod follows the language selected **inside the game**. Changing it closes the mod's open panels; reopen them in the new language. A saved, unedited built-in prompt switches between Chinese and English automatically. Custom prompts are never translated or overwritten. A changed prompt clears old model conversation context on the next message. Chat history is held in memory only.

## Local settings and network use

Opening settings or saving them does not contact the provider. Fetching models, sending a test, chatting and running assistant commands use the configured API endpoint. Queried game context and tool results are sent as part of the conversation when the model uses tools.

API keys are used only for your configured endpoint and are not put into chat or logs. With **Remember API key** enabled, the key is stored in plain text in the game's per-mod data folder (`ModData/probably_ai/connection.local.json`). Disable it and save for a session-only key. Do not share your local settings file.

The Workshop upload contains only this mod's compiled DLL, manifest, player guides, portrait and cover. It includes no personal connection settings, save files, account data, source cache, game DLLs or debug symbols. Only the existing **network** capability is declared.

Version 0.6.1. Built against the installed native mod API (application version 0.46D / VERSION 100-Rev3). Automated checks use simulated game state and HTTP; in-game behavior and provider compatibility still need player testing. Character and cover artwork were generated with AI from the supplied character reference. This is an independent fan mod, not an official DeepSeek product.
