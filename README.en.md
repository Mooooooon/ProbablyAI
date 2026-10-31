# Deep Space AI: Probably DeepSeek

A native Probably Stolen mod featuring Xiaojing, a pixel-art whale-girl maid assistant. Chinese and English UI, built-in prompts and tool descriptions follow the game's language setting automatically. Other languages use English. Your custom prompt and your own text are preserved.

GitHub: [https://github.com/Mooooooon/ProbablyAI](https://github.com/Mooooooon/ProbablyAI) | Steam Workshop: [https://steamcommunity.com/sharedfiles/filedetails/?id=3830081737](https://steamcommunity.com/sharedfiles/filedetails/?id=3830081737)

## Getting started

1. Subscribe in Steam Workshop, enable the mod in the game's Mods menu, review its **network** capability and restart the game.
2. Enter your pawnshop. Click Xiaojing's portrait to chat. **F6** toggles the portrait; **F7** opens assistant settings.
3. Enter an OpenAI-compatible base URL, API key and model. Defaults: `https://api.deepseek.com`, `deepseek-flash`. You can fetch the provider's model list or enter a model ID manually.
4. Choose a **Thinking level**: follow model default, thinking off, low, high or max. Low/High/Max send `thinking: {"type":"enabled"}` with `reasoning_effort`; thinking off sends `thinking: {"type":"disabled"}` only. Keep the default if your service rejects these fields.
5. View or edit the full system prompt in F7. The default maid persona addresses you as **Master**, speaks warmly, and avoids emoji and emoticons unless requested. Save settings to apply changes to future messages, or preview the current draft with a test message.

You need your own compatible API service and credentials. Provider charges are handled by your chosen provider. The mod contains no shared API key or included API credits.

The chat window remembers its dragged position when closed and reopened, including after restarting the game. Its position is saved separately in local `ModData/probably_ai/chat-window.local.json`; this file contains no chat or API key and is excluded from uploads.

## Things to try

- "Put the food in my inventory on the counter."
- "What does the current customer want, and how much budget do they have left?"
- "Find items this customer would buy and can afford. Tell me first."
- "Move the note in Archives → Mentor Notes to the counter."
- "Put the scrap metal into the furnace's input compartment."

The assistant queries live containers and items, including nested folders, bags, storage pages and machine compartments. It can inspect items, move them through the game's native rules and open or close container windows. Customer queries include buying preferences, remaining purchase budgets and barter, procurement and gunsmith requirements. Top-level counter items use the native live quote. Owned items preview variety bonuses, current price events, retail markup and known customer effects before placement, using the game's innate, market and final negotiation stages. Budget checks use that complete quote and return a price breakdown; percentages must not be applied twice. Multiple items require a combined budget check. Recheck after placement, inspection or negotiation, since custom callbacks may affect the final quote.

If the visitor matches a wanted poster, `get_current_customer` returns `wanted` with `is_wanted`, `stealth` and a ready-to-use `quiet_reminder`. Xiaojing adds a quiet parenthetical reminder on a new line at the end of the reply, e.g. “(whispering) Master, this person matches the wanted poster.” She reminds once, never reveals the wanted status, bounty or real name to the customer, and never reports, alerts or changes the trade automatically. Visitors already reported or cleared from the poster are not reminded again.

The input row also has **Quick replies**:

- **Pick goods for this customer's needs** combines the current customer's requirements, budget and stock, then places suitable goods on the counter.
- **Appraise what the customer brought** inspects each item in the customer window.

Quick replies send immediately and keep any draft in the input. The default prompt prioritizes finishing Master's task and ends without a closing question, confirmation request or unsolicited next step; it asks one short question only when essential information is missing and the task cannot be done safely.

The mod does not complete purchases or sales, choose dialogue options, unlock containers, spawn items or start machines. Native game callbacks and automatic processing still behave normally. Moves can partially succeed. Stopping a request cancels later actions; completed moves remain. The model must support Chat Completions tool calling to use game tools.

## Language and prompts

The mod follows the language selected **inside the game**. Changing it closes the mod's open panels; reopen them in the new language. A saved, unedited built-in prompt switches between Chinese and English automatically and upgrades to the current default. Custom prompts are never translated or overwritten. A changed prompt clears old model conversation context on the next message. Chat history is held in memory only. Old query data is omitted from model history and past operations are marked historical, while the visible transcript is preserved. Each new message refreshes the current customer and counter items, quantities and ownership; sold goods are not treated as unsold merely because an object remains on the counter. Failed or incomplete observations must not be filled in from chat history. Routine replies report prices and affordability directly, explaining sources or limitations when relevant. The F7 test chat and the assistant commands on the manual controls page are independent single-turn requests; they do not share the chat window's context.

## Mechanical hand, cigarette and food stamp inspection

After unlocking visual inspection in the game, ask “Appraise this mechanical hand,” “Check the cigarettes on the counter,” or “Is this food stamp genuine?” Both chat and F7 assistant commands offer these read-only tools:

- `appraise_mechanical_hand` reads material, control interface, markings, wear, armor/blade addons, sets and the native per-unit appraisal base value. It already includes set bonuses and is separate from a trade quote or appraisal fee.
- `inspect_cigarette` compares visible seal, logo, lettering and packaging with the standard sample. It reports apparent authenticity and visible defects; missing or unsupported appearance is inconclusive. It does not read hidden authenticity or issue flags.
- `inspect_food_stamp` reads the visible security seal, logo, chip, lettering and printed `face_value`. It reports apparent authenticity and visible defects. Stamps are issued only in 40/80/120/160/200/240 credits; a seal is expected from 160 credits, and a missing or damaged chip makes the stamp unredeemable. It does not read hidden authenticity or issue flags.

Each call refreshes the shared item tree, including accessible nested containers, and respects native locks and scene restrictions. These tools do not submit estimates, expose features, confront customers, redeem stamps or complete orders. Submit results manually in the game.

## Local settings and network use

Opening settings or saving them does not contact the provider. Fetching models, sending a test, chatting and running assistant commands use the configured API endpoint. Each chat message or assistant command includes freshly read customer and counter context. Other queried game context and tool results are sent when the model uses tools. Automatic observations are ephemeral and do not add messages to the visible transcript or stored conversation.

API keys are used only for your configured endpoint and are not put into chat or logs. With **Remember API key** enabled, the key is stored in plain text in the game's per-mod data folder (`ModData/probably_ai/connection.local.json`). Disable it and save for a session-only key. Do not share your local settings file.

The Workshop upload contains only this mod's compiled DLL, manifest, player guides, portrait and cover. It includes no personal connection settings, save files, account data, source cache, game DLLs or debug symbols. Only the existing **network** capability is declared.

Version 0.7.0. Built against the installed native mod API (application version 0.46D / VERSION 100-Rev3). Automated checks use simulated game state and HTTP; in-game behavior and provider compatibility still need player testing. Character and cover artwork were generated with AI from the supplied character reference. This is an independent fan mod, not an official DeepSeek product.
