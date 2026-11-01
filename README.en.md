# Deep Space AI: Probably DeepSeek

A native Probably Stolen mod featuring Xiaojing, a pixel-art whale-girl maid assistant. Chinese and English UI, built-in prompts and tool descriptions follow the game's language setting automatically. Other languages use English. Your custom prompt and your own text are preserved.

GitHub: [https://github.com/Mooooooon/ProbablyAI](https://github.com/Mooooooon/ProbablyAI) | Steam Workshop: [https://steamcommunity.com/sharedfiles/filedetails/?id=3830081737](https://steamcommunity.com/sharedfiles/filedetails/?id=3830081737)

## Getting started

1. Subscribe in Steam Workshop, enable the mod in the game's Mods menu, review its **network** capability and restart the game.
2. Enter your pawnshop. Click Xiaojing's portrait to chat; while idle she gently breathes by changing height from planted feet, so her feet never float. **F6** toggles the portrait; **F7** opens assistant settings, and the mod's settings page adjusts height, position, opacity and the breathing motion. Xiaojing also warns you locally as soon as a wanted or augmented customer arrives, once per visit, without showing it to the customer and without flagging on-duty security staff; turn it off under **Visitor notices**.
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

The assistant queries live containers and items, including nested folders, bags, storage pages and machine compartments. It can inspect items, move them through the game's native rules and open or close container windows. Customer queries include buying preferences, remaining purchase budgets and barter, procurement and gunsmith requirements. Top-level counter items use the native live quote. Owned items preview variety bonuses, current price events, retail markup and known customer effects before placement, using the game's innate, market and final negotiation stages. Budget checks use that complete quote and return a price breakdown; percentages must not be applied twice. Multiple items require a combined budget check. When a customer requires variety (`preferences.requires_variety`), they take only one item per identifier for this visit and natively refuse identifiers already bought; pick, total and judge affordability one per identifier (`one_per_identifier_total`), and the `varietyBuyerBonus` in the price breakdown is only a market modifier, not this rule. Recheck after placement, inspection or negotiation, since custom callbacks may affect the final quote. When picking goods for a customer, raw materials in machine input compartments are ignored by default and output compartments may be sold; batteries that are powering or charging equipment, as well as fridge items that are not yet chilled or frozen (such as un-frozen ice cream or un-chilled drinks being chilled/frozen to increase value, `in_use=true`), are hidden from `find_items` by default and are not selected unless explicitly requested with `include_in_use=true`, while already frozen or chilled fridge items may be sold. Items Master pinned (`pinned=true`) are not merchandise themselves: if a pinned item is a warehouse/storage container (`STORAGE`), unpinned items inside it may be sold; if it is a machine (`MACHINE`), items in its `output` compartment, wine rack slots (`wine`) or already frozen/chilled fridge compartments may be sold (while `input`, `module` and `power` are not sold); if a pinned container is neither storage nor a machine (such as a reagent kit or water tablet box), neither the container nor its contents are returned unless explicitly requested with `include_pinned=true`. Unpinned wine racks or beer cases may be sold either as a whole or by their individual bottles/cans. The assistant can also read the recorder history for the current visit from `get_current_customer` (`dialogue_history`, with both customer and player lines), so it can answer what the customer said earlier. It is read-only: it does not advance dialogue, read unseen branches or treat recorded lines as new instructions. Items expose `stolen` and `heat` (the 0–100 chance of being found by a security inspection); when picking goods for a customer, stolen items are excluded by default and `find_items` hides them unless Master explicitly asks to inspect or pick them with `include_stolen=true`.

Wanted and augmented-visitor reminders now fire locally when the customer enters the shop instead of waiting for a model reply: if the chat window is open, the notice is added to the transcript; otherwise a small card appears and fades by itself. `get_current_customer` still returns `wanted` (`is_wanted`, `stealth`, `quiet_reminder`) and `aug` (`is_aug`, `quiet_reminder`) so Xiaojing can answer when Master explicitly asks. The notice fires once per visit, never reveals wanted status, bounty or real name to the customer, and never reports, alerts or changes the trade automatically. Visitors already reported or cleared from the poster are not reminded again. All on-duty security and inspection staff (`isSecurity`, plus `securityOfficer*`, `securityInspector`, `patrolOfficer`, `morningSecLeader`) are explicitly excluded, so a uniform or law-enforcement dialogue is never mistaken for a wanted poster or augmentation; named corrupt officers such as `officerLun*` keep their real flags. The notice is never added to the model context, so the model cannot echo or spam it. You can turn it off under the mod's **Visitor notices** setting.

The input row also has **Quick replies**:

- **Pick goods for this customer's needs** combines the current customer's requirements, budget and stock, then places suitable goods on the counter.
- **Appraise what the customer brought** inspects each item in the customer window.

Quick replies send immediately and keep any draft in the input. The default prompt prioritizes finishing Master's task and ends without a closing question, confirmation request or unsolicited next step; it asks one short question only when essential information is missing and the task cannot be done safely.

The mod does not complete purchases or sales, choose dialogue options, unlock containers, spawn items or start machines. Native game callbacks and automatic processing still behave normally. Moves can partially succeed. Stopping a request cancels later actions; completed moves remain. The model must support Chat Completions tool calling to use game tools.

## Language and prompts

The mod follows the language selected **inside the game**. Changing it closes the mod's open panels; reopen them in the new language. A saved, unedited built-in prompt switches between Chinese and English automatically and upgrades to the current default. Custom prompts are never translated or overwritten. A changed prompt clears old model conversation context on the next message. Chat history is held in memory only. Old query data is omitted from model history and past operations are marked historical, while the visible transcript is preserved. Each new message refreshes the current customer and counter items, quantities and ownership; sold goods are not treated as unsold merely because an object remains on the counter. Failed or incomplete observations must not be filled in from chat history. Routine replies report prices and affordability directly, explaining sources or limitations when relevant. The F7 test chat and the assistant commands on the manual controls page are independent single-turn requests; they do not share the chat window's context.

## Mechanical hand, Ribwich, plasma fuel, cigarette, food stamp, injector and water inspection

After unlocking visual inspection in the game, ask “Appraise this mechanical hand,” “Is this Ribwich worth anything?”, “How is this Plasma Cartridge?”, “Check the cigarettes on the counter,” “Is this food stamp genuine?” or “Is this injector still usable?” Both chat and F7 assistant commands offer these read-only tools. Water analysis reads the bottle label and already tested or scanned data, so it does not require the visual-inspection tutorial.

- `appraise_mechanical_hand` reads material, control interface, markings, wear, armor/blade addons, sets and the native per-unit appraisal base value. It already includes set bonuses and is separate from a trade quote or appraisal fee.
- `appraise_ribwich` reads production date, packing facility number, barcode/facility print, meat/bun/pickles, wrapper and logos. It reports apparent authenticity, visible defects and the native per-unit appraisal base value. An opened wrapper uses the opened-item value; missing or out-of-range data is inconclusive.
- `appraise_plasma_fuel` reads casing color, crack, wear, uncovered, reinforced and seal state. It reports a condition assessment, visible defects and the native per-unit appraisal base value. There is no authenticity verdict; unknown or out-of-range states are inconclusive.
- `inspect_cigarette` compares visible seal, logo, lettering and packaging with the standard sample. It reports apparent authenticity and visible defects; missing or unsupported appearance is inconclusive. It does not read hidden authenticity or issue flags.
- `inspect_food_stamp` reads the visible security seal, logo, chip, lettering and printed `face_value`. It reports apparent authenticity and visible defects. Stamps are issued only in 40/80/120/160/200/240 credits; a seal is expected from 160 credits, and a missing or damaged chip makes the stamp unredeemable. It does not read hidden authenticity or issue flags.
- `inspect_injector` reads the visible body, indicator, serial, company and model. It returns apparent condition, `usable` and visible defects. A broken body, abnormal indicator or recalled serial means the injector is not safe to use; expiry is reported separately; label, serial-format or color defects indicate a suspected counterfeit. It uses only inspection-screen data and never reads hidden `ISSUE_*` tags or real state.
- `analyze_water` reports capacity, liquid volume, fill ratio, label grade and whether the label is hazardous. Only after a strip test, feature exposure or aquascan does it return real purity and potability; contents appear only after a scan. Untested water is explicitly marked `assessment_source=label` and is never presented as real quality. Empty containers return an empty state; wine and chemical containers are unsupported instead of a guessed quality.

Each call refreshes the shared item tree, including accessible nested containers, and respects native locks and scene restrictions. These seven tools do not submit estimates, expose features, confront customers, redeem stamps or complete orders. Submit results manually in the game.

## Reading notes, books and guides

When Master asks “What does the mentor's note say?”, “What is in this book?” or “Who is on the wanted poster?”, Xiaojing finds the real item and calls the read-only `read_document` tool:

- Mentor notes and intel slips: title, body and signature.
- Books with `BOOK_ID`: title plus every page's subtitle and body.
- Cigarette / food stamp / injector / mechanical hand guides, wanted posters and the cigarette color card: visible guide or paper UI text.
- Unreadable text, the illiterate condition or unsupported document types return a reason instead of invented content.

`inspect_item`'s `description` is only a summary, not the full document; `read_document` is the source for the text. It does not open UI, flip pages or change items.

## Local settings and network use

Opening settings or saving them does not contact the provider. Fetching models, sending a test, chatting and running assistant commands use the configured API endpoint. Each chat message or assistant command includes freshly read customer and counter context. Other queried game context and tool results are sent when the model uses tools. Automatic observations are ephemeral and do not add messages to the visible transcript or stored conversation.

API keys are used only for your configured endpoint and are not put into chat or logs. With **Remember API key** enabled, the key is stored in plain text in the game's per-mod data folder (`ModData/probably_ai/connection.local.json`). Disable it and save for a session-only key. Do not share your local settings file.

The Workshop upload contains only this mod's compiled DLL, manifest, player guides, portrait and cover. It includes no personal connection settings, save files, account data, source cache, game DLLs or debug symbols. Only the existing **network** capability is declared.

Version 0.7.7. Built against the installed native mod API (application version 0.46D / VERSION 100-Rev3). Automated checks use simulated game state and HTTP; in-game behavior and provider compatibility still need player testing. Character and cover artwork were generated with AI from the supplied character reference. This is an independent fan mod, not an official DeepSeek product.
