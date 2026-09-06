# Инструкции оператора llama.cpp Windows Manager

## Назначение

llama.cpp Windows Manager — это Windows-приложение WPF, которое владеет своим рабочим пространством (workspace),
состоянием SQLite, инвентарём runtime и моделей, загрузками, контролируемыми
сеансами `llama-server`, OpenAI-совместимым gateway, журналами и живыми метриками.

`llwmctl` — поддерживаемый интерфейс автоматизации. Он взаимодействует с аутентифицированным
loopback API управления внутри запущенного Manager, поэтому успешные команды обновляют
реальное состояние приложения и отображаются в UI.

## Обязательные правила

- **Бренд форка — «(ext)»**: заголовок окна (`MainWindow.xaml` Title) и `App.Title`
  во ВСЕХ локализациях обязаны содержать суффикс `(ext)` — пользователь должен
  видеть, что это расширенная версия, а не оригинал. Не терять при имплементации
  апстрима!
- **Схема версий (ext)**: от базы 2.3.2 версии идут **2.3.2.1 → 2.3.2.2 → ...**
  (инкремент четвёртого числа). НЕ своевольничать с нумерацией — только по
  указанию пользователя. Все релизы — с пометкой (ext).
  `LocalLlmConsole.App.csproj` (Version/AssemblyVersion/FileVersion/InformationalVersion),
  **`LocalLlmConsole.Service.csproj` (Version)**, **`LocalLlmConsole.Updater.csproj` (Version)**,
  `MainWindow.xaml` (Title), `installer/LlamaCppWindowsManager.iss` (AppVersion),
  `MainWindow.State.cs` (AppVersionLabel). Забытая константа = неверный бейдж версии!
  Проверка: `grep -rn "<старая версия>" src/ installer/` — после бампа не должно
  остаться ни одного вхождения старой версии вне истории git.
- **Разметка страниц — DockPanel**: при добавлении ЛЮБОГО нового элемента в страницу
  (Ui/Pages/*) ОБЯЗАТЕЛЬНО добавлять `DockPanel.SetDock(элемент, Dock.Top)` в блок
  «Set DockPanel alignment» в конце фабрики — иначе элемент причалится слева
  и сдвинет всю разметку ниже!
- **НИКОГДА не мержить!** Ни апстрим-ветки, ни локальные ветки, ни pull request'ы —
  любые действия по слиянию веток запрещены без явной команды пользователя.
  Ветка `agent/service` — рабочая; `main` — витрина, её не трогать.
- **Минимальное вмешательство в оригинальный (вендорский) код.** Лучше десять
  своих классов, чем десять правок в оригинальном классе. Изменения вносятся
  в новые файлы (partial-классы, расширения, отдельные сервисы); правка
  вендорского файла допустима только если без неё никак — и требует явного
  одобрения пользователя.
- **Обновления оригинала (upstream) по умолчанию игнорируются.** Забирать
  только конкретные новые возможности, если пользователь явно попросит,
  и точечно, не мержем целиком.
- Вендорские доки (README оригинала, AGENTS, docs/*) не изменять; фичи форка
  описываются в отдельных файлах (например, `docs/LLAMAMANAGER-SERVICE.md`).
- Этот список правил будет дополняться по мере необходимости — он обязателен
  для любого агента, работающего в репозитории.

## ⚠️ Релиз: ЧИСТЫЙ дистрибутив — ЕДИНАЯ точка `scripts/build-ext-release.ps1`!

- **Пользовательский профиль НЕ переписывать автоматически!** Если GGUF
  расходится с сохранённым ContextSize — это НЕ баг «не пересчитался»:
  ContextSize в профиле = ручная настройка. Для перечитывания есть кнопка
  **«⟳ GGUF»** рядом с полем «Размер контекста» (страница Модели): она
  читает context_length из метаданных модели и подставляет в поле — юзер
  сам жмёт Save Profile. (Поля: LaunchSettingUiSchema
  `ContextSizeReload` → `RegisterContextSizeReloadEditor` → контроллер
  `ReloadContextSizeFromModelAsync`; GGUF читается в фоне.)
- **Сборку релиза делать ТОЛЬКО через `scripts/build-ext-release.ps1`**
  (см. «Релизный цикл» п.2) — НЕ руками и НЕ вендорским `publish-app.ps1`
  в одиночку: он не собирает Updater и не делает zip «ровно 4 exe».
- `build-ext-release.ps1` внутри зовёт вендорский `publish-app.ps1`
  (App/llwmctl/Service со сжатием, доки, **чистка всех `*.pdb`**), затем
  публикует Updater, зачищает мусор (pdb/runtimeconfig/deps/hashes.txt),
  пересчитывает sha256 для 4 exe, делает zip из 4 exe и зовёт
  `build-installer.ps1 -SkipPublish` (Setup).
- НИКОГДА не собирать вручную `dotnet publish` в dist-папку и не делать
  `Compress-Archive` из неё: туда попадают `*.pdb`, `*.runtimeconfig.json`,
  временные `hashes.txt`, доки — и всё это уезжает в релизный zip!
- **ZIP релиза = РОВНО 4 exe**: `LlamaCppWindowsManager.exe`,
  `LocalLlmConsole.Service.exe`, `LocalLlmConsole.Updater.exe`, `llwmctl.exe`
  (llwmctl ~37.6 МБ со сжатием, НЕ 73 МБ!). Ничего больше.
- Setup (.iss) берёт файлы явным списком — там мусор не появляется, но
  пересобирать Setup ПОСЛЕ любой пересборки exe (иначе в Setup попадёт
  старая версия llwmctl).
- **Контроль перед заливкой на GitHub**: `unzip -l zip` — должно быть 4 файла,
  `grep -cE "\.exe"` = 4, посторонних = 0. Setup пересобирать в последнюю очередь.
- Перезаливка ассетов: удалить старые (DELETE releases/assets/{id}) → залить
  новые (HTTP 201). Версию НЕ бампать при перезаливке тех же файлов.

## ⚠️ Контекстное окно и параллельные запросы (Hermes/API) — НЕ ПУТАТЬ

- **Источник правды для контекста модели — GGUF** (`context_length` через
  `ModelCapabilityService.ContextLength`). GGUF недоступен → эвристика
  `AppSettings.DefaultContextSize` (128K).
- НИКАКИХ «трюков» с хардкодом/делением в профиле: у qwen НЕ ставить
  фиксированный 524288 (исторический баг: для 256K-моделей завышал вдвое,
  для 1M — занижал). ContextSize профиля = GGUF как есть.
- **llama-server при `--parallel N` САМ делит контекстное окно на N слотов**
  и обрезает до n_ctx_train. Клиент (Hermes) берёт `context_length` из
  `/v1/models` и по нему планирует сжатие (обычно с 50%).
- **В API наружу отдавать реально доступный контекст одного запроса:**
  `min(ContextSize, n_ctx_train) / parallel_slots` (ModelGatewayResponseWriter).
  Иначе клиент думает что окно 1M, а слоту реально достаётся 1M/N —
  сжатие наступает слишком поздно, контекст переполняется.
- Поле «Параллельные запросы» (ParallelSlots) — в разделе «Сервер» настроек
  модели; от него именно ДЕЛИМ контекст в ответах API. При смене потоков
  ContextSize в профиле НЕ пересчитывать (он остаётся GGUF).

## CRLF/кодировки на Windows — вечная боль, НЕ наступать дважды

- Файлы, созданные PowerShell (`Add-Content`, `Out-File`, `Get-Content ... | ...`) —
  **UTF-16 или CRLF**. Читать их bash-циклами (`while read ...; do ...`) НЕЛЬЗЯ:
  `\r` прилипает к последнему слову строки и ломает имена/значения
  (например, создаёт файл `LlamaCppWindowsManager.exe\r.sha256`).
- Обходы:
  1. PowerShell → файл → читать `read_file` (НЕ bash-инструментами);
  2. Если нужно скормить содержимое bash: писать в файл через
     `[System.IO.File]::WriteAllText(path, text)` (UTF-8 без BOM, LF) или
     `write_file` (Hermes), затем читать bash'ем;
  3. Для генерации набора `.sha256`-файлов из общего списка — писать каждый
     файл отдельной командой `[System.IO.File]::WriteAllText` (НЕ циклом
     `while read` по CRLF-файлу), либо сначала прогнать через `tr -d '\r'`.
- `write_file` пишет LF — для .bat/.cmd/`.iss` СТРОГО нужен CRLF (см. правила
  бати); для промежуточных txt/hash-файлов LF допустим.
- Вывод `dotnet test`/PowerShell в terminal может приходить в UTF-16
  (кракозябры `呓呁...`) — писать в файл и читать read_file.

## ⚠️ Чеклист: НЕ СДВИНУТЬ ПОЗИЦИОННЫЕ АРГУМЕНТЫ (урок 2.3.2.7)

record `AppUpdateInfo` (AppUpdateService.cs) рос: добавились Service/Updater/Zip-блоки
с полями `*ExpectedSha256`, а парсер `AppUpdateReleaseParser` создавал его ПОЗИЦИОННО →
все поля после вставки сдвинулись на 1: `ZipAssetUrl` получил URL `.sha256`-файла,
`ZipChecksumAssetUrl` остался пустым и ушёл в fallback на чексумму **exe**.
Итог: GUI скачал текстовый `.sha256`-файл как «обновление» и упал:
«SHA-256 не совпадает: ожидалось a2a0f (exe), получено D0B871 (текст)».

ПРАВИЛА (навсегда, для всего кода этого репозитория):
1. Любой record/конструктор с 3+ параметрами создавать ТОЛЬКО именованными
   аргументами (`Field: value`). Позиционные аргументы запрещены — record растёт,
   и тихий сдвиг ломает логику без ошибки компиляции.
2. При ДОБАВЛЕНИИ параметра в record: `grep -rn "new <RecordName>(" src/`
   — проверить ВСЕ места создания (их может быть несколько, и не только в том
   файле, где объявлен record).
3. Схема обновления проверяется ТОЛЬКО прогоном Updater'а на тестовом каталоге
   (updtest) с фейковым релизом: файлы должны замениться, SHA сойтись.
4. При жалобе «SHA-256 не совпадает» — сравнить ожидаемое значение с фактическими
   хэшами ассетов (remote .sha256 vs локальный расчёт): если «ожидалось» = хэш
   другого файла (exe вместо zip) — это сдвиг полей, а не битое скачивание.

## Схема обновления (zip-режим, с 2.3.2.6) — НЕ ломать

- GUI качает ZIP + свежий `LocalLlmConsole.Updater.exe` в `<App>\LlamaUpdate\<версия>\`
  (рядом с приложением — при нескольких копиях ничего не перемешивается),
  проверяет SHA-256 каждого; ошибка → GUI продолжает работать.
- Updater запускается отдельным процессом ТОЛЬКО после 100% успеха:
  `--update-zip <zip> --target-dir <dir> --service-name <имя>`; распаковывает ZIP,
  ИСКЛЮЧАЯ себя (`LocalLlmConsole.Updater.exe`); старый файловый режим
  `--app-file/--service-file` сохранён для совместимости.
- Ждёт закрытия ТОЛЬКО своего `LlamaCppWindowsManager.exe` (лежащего рядом), 5 сек → KILL.
- UAC — только при установленной службе; отказ → выход без изменений.
- Запуск GUI в конце — через `explorer.exe`, только для настоящего PE (MZ-проверка).
- Лог: `{TargetDir}\LlamaUpdate\update.log`.
- Кнопки/выбор на странице «Управление службой» должны брать путь среды выполнения
  ИЗ БД (runtimes), а не из устаревшего service-config.json; страница перечитывает
  среды при каждом показе (`ReloadSelectionsAsync`), конфиг пересохраняется
  (`SaveSelection` после восстановления выбора).

## Службы Windows (форк ext) — несколько копий = несколько служб

- Имя службы и DisplayName ВЫЧИСЛЯЮТСЯ из каталога установки, НЕ хардкодятся.
  Единое правило — `LocalLlmConsole.Core/Services/ServiceIdentity.cs`:
  - serviceName: `llama-cpp-` + путь без `:\`, `\`→`_`, lower
    (`D:\NEURO\LlamaGPU` → `llama-cpp-d_neuro_llamagpu`);
  - DisplayName: `Llama.cpp (D:\NEURO\LlamaGPU)` — ПОЛНЫЙ путь каталога
    (не `GetDirectoryName`, который срезает последний сегмент!).
- Две копии из разных каталогов (LlamaManager, LlamaGPU, ...) = две независимые
  службы. Всё, что ищет «свою» службу/процесс (GUI, Updater), ищет по имени,
  вычисленному из СВОЕГО каталога, и ждёт закрытия ТОЛЬКО своего экземпляра
  `LlamaCppWindowsManager.exe`.
- **Single-instance — тоже по каталогу** (App.xaml.cs): имя mutex вычисляется из
  каталога установки (как имя службы), НЕ глобальный `...-single-instance`!
  Иначе вторая копия из другого каталога не стартует («already running»).
  Control-API (llwmctl) — с fallback по портам, конфликт не блокирует старт.
- **Миграция legacy `llama-cpp-server`**: если найдена служба с таким именем и
  её ImagePath указывает в текущий каталог — GUI показывает жёлтый баннер и кнопку
  «Перенести службу» (`LlamaServiceViewModel.DetectLegacyService/MigrateCommand`);
  CanInstall блокируется, пока legacy не перенесена.
- Служба хранит конфиг запуска в `data/state/service-config.json` (ExecutablePath
  среды, аргументы, ModelId/ProfileId/RuntimeId). ВАЖНО: при переезде/переименовании
  каталога пути в этом файле устаревают — GUI обязан перечитывать актуальные пути
  из БД (таблицы runtimes/models) и пересохранять конфиг, а НЕ полагаться на
  сохранённые абсолютные пути.

## Релизный цикл (только по команде пользователя!)

Порядок (не менять!):
1. **Бамп версии** — инкремент 4-го числа (2.3.2.x), grep по старой версии по
   ВСЕМ 6 местам (см. «Обязательные правила»); бейдж `(ext)` в AppVersionLabel
   и Title обязателен.
2. **ЕДИНАЯ ТОЧКА СБОРКИ — `scripts/build-ext-release.ps1`** (НЕ собирать
   руками `dotnet publish` в dist!):
   ```
   powershell -ExecutionPolicy Bypass -File scripts/build-ext-release.ps1
   ```
   Скрипт сам: зовёт вендорский `publish-app.ps1` (App + llwmctl + Service
   single-file со сжатием + доки/лицензии + sbom, чистит *.pdb), затем
   публикует **Updater** (вендорский publish-app.ps1 про него НЕ знает!),
   зачищает мусор (pdb/runtimeconfig/deps/hashes.txt), пересчитывает sha256
   для всех 4 exe, делает **zip РОВНО из 4 exe** (эталон 2.3.2.8!) и зовёт
   `build-installer.ps1 -SkipPublish` для Setup. Опции: `-InnoSetupPath`,
   `-SkipInstaller`.
3. **ZIP релиза = РОВНО 4 exe**: `LlamaCppWindowsManager.exe`,
   `LocalLlmConsole.Service.exe`, `LocalLlmConsole.Updater.exe`, `llwmctl.exe`
   (~37.6 МБ со сжатием!). Ни pdb, ни доков, ни sha256-файлов внутри zip.
   Скрипт сам это проверяет (ровно 4 записи) — но перед заливкой всё равно
   контроль: `unzip -l dist/LlamaCppWindowsManager-win-x64.zip` → 4 файла.
4. Git: commit → push → тег `2.3.2.X` **на HEAD** (тег на старый коммит даёт
   релизу старую дату и уводит его вниз списка!) → push тега → GitHub Release
   **«2.3.2.X (ext)»** → загрузка 10 ассетов (5 файлов + 5 .sha256):
   App, Service, Updater, win-x64.zip, Setup. Проверить: 10/10, порядок релизов
   сверху вниз, ни один ассет не пропал (ошибки публикации «съедают» файлы тихо).
5. Версии ассетов проверить через FileVersion (все exe = 2.3.2.X).
6. Перезаливка ассетов без бампа: DELETE `releases/assets/{id}` → залить новые
   (HTTP 201); Setup пересобирать ПОСЛЕ любой пересборки exe.

- Используйте `llwmctl` для операций с живым Manager. Не редактируйте базу данных SQLite,
  не открывайте API управления и не автоматизируйте элементы управления WPF. Не запускайте `llama-server`
  напрямую.
- Начинайте каждую операционную задачу с `llwmctl status`.
- Запускайте `llwmctl capabilities` и `llwmctl operations list` перед использованием
  незнакомого поля или действия. Живые схемы (live schemas) являются авторитетными.
- Запускайте `llwmctl self` перед работой, которая может выгрузить, перезапустить, заменить,
  обновить или иным образом затронуть загруженную модель.
- Считайте ненулевой код выхода CLI или JSON-ответ с `"ok": false` признаком сбоя.
  Сохраняйте и сообщайте возвращённую ошибку.
- Никогда не используйте `--confirm` или `--allow-self-stop`, если пользователь явно
  не санкционировал заявленное последствие.

## Выбор правильного CLI и рабочего пространства

Рядом с установленным или переносимым приложением используйте соответствующий исполняемый файл:

```powershell
./llwmctl.exe status
```

Из исходного репозитория используйте собранный CLI, находящийся на `PATH`, или:

```powershell
dotnet run --project src/LocalLlmConsole.ControlCli/LocalLlmConsole.ControlCli.csproj -- status
```

Если обнаружение неоднозначно, укажите рабочее пространство или файл обнаружения:

```powershell
llwmctl status --workspace <workspace>
llwmctl status --connection <workspace>\state\control.json
```

Записываемая переносимая установка обычно использует `<application-folder>\data`.
Никогда не читайте, не выводите, не копируйте и не расшифровывайте вручную контрольный токен.

Если Manager недоступен, а пользователь попросил запустить его или управлять им, запустите
`LlamaCppWindowsManager.exe` обычным и видимым образом, затем повторите `status`. Приложение
является однокопийным (single-instance) в рамках сеанса пользователя Windows; не запускайте второй Manager.

## Первый контакт и холодный старт

Выполните эти команды перед выбором идентификаторов модели, runtime, профиля или сеанса:

```powershell
llwmctl status
llwmctl capabilities
llwmctl operations list
llwmctl self
llwmctl models list
llwmctl runtimes list
llwmctl sessions list
```

Используйте `profiles list --model <model>` для сохранённых вариантов. Когда `self`
неоднозначен, повторите с подсказками `--endpoint`, `--model`, `--session`, `--port` или процесса;
никогда не угадывайте по выбору в UI.

## Загрузка, перезапуск и выгрузка моделей

Предпочитайте сохранённый профиль и ждите готовности endpoint:

```powershell
llwmctl load <model> --profile <profile> --wait
```

Повторяющиеся параметры `--set name=value` являются разовыми переопределениями. Сохраняйте их только
по запросу через `--save-profile=<name>` или команды профилей. Полные имена настроек и принимаемые
значения получайте из `capabilities`.

Перед любым перезапуском или выгрузкой определите текущую модель с помощью `self`. Никогда
не останавливайте сеанс, обслуживающий текущую операцию, если пользователь явно не попросил
об этом последствии и не принял, что ответ может оборваться. Только тогда можно
использовать `--allow-self-stop`. Не используйте `--unload-others`, пока идентичность
неизвестна.

## Компаньоны и профили запуска

Проверьте совместимые вспомогательные файлы перед выбором файлов vision, draft или MTP:

```powershell
llwmctl models companions <model>
```

Автоматическое обнаружение ограничено точной папкой модели. Явно совместимые пути могут
находиться в другом месте. Поля профиля: `visionProjectorPath`, `specDraftModelPath`,
`mtpHeadPath` и `speculativeType`.

Для восходящего `draft-mtp` оставляйте `specDraftModelPath` пустым, когда основной GGUF
сообщает `embeddedDraftMtp: true`; тогда Manager использует встроенные тензоры NextN/MTP.
Используйте `visionProjectorPath=embedded` только когда выбранные runtime и пакет модели
явно поддерживают встроенный мультимодальный проектор.

## Группы моделей и удержание (retention)

Группы назначаются профилям запуска, а не напрямую записям моделей:

```powershell
llwmctl groups list
llwmctl groups create --name "Interactive" --retention pinned --priority high
llwmctl groups create --name "Batch" --retention idle-timeout --idle-minutes 15 --priority low
llwmctl groups assign <model> <profile> --group "Batch"
llwmctl groups unassign <model> <profile>
```

Допустимые режимы удержания: `inherit`, `pinned` и `idle-timeout`; приоритеты: `low`,
`normal` и `high`. Загрузка группы выполняет предварительную проверку дублирующихся
назначений моделей, runtimes, портов и суммарной VRAM перед запуском чего-либо.
Удержание влияет на автоматическую выгрузку по бездействию, а не на планирование вывода.
Явные операции жизненного цикла и политика gateway «Single active» по-прежнему
имеют приоритет.

## Наблюдение за сеансами, журналами и загрузками

```powershell
llwmctl sessions inspect <session>
llwmctl gateway inspect
llwmctl sessions metrics <session>
llwmctl sessions logs <session>
llwmctl logs list
llwmctl logs tail
llwmctl hf search <query>
llwmctl hf download --repo <owner/repo> --file <path.gguf>
llwmctl jobs list
```

Приостановите, возобновите или отмените загрузку модели с помощью `jobs pause|resume|cancel
<job-id>`.

Работа с исходным кодом runtime должна следовать поэтапному потоку операций: выполните
`runtime-source.check`, затем `runtime-source.download`, затем `runtime-build.start`
с загруженным исходным кодом, возвращённым `runtime.catalog`. Используйте
`operations run <name> --dry-run --set name=value` перед операциями
с последствиями.

## Настройки приложения и видимость в UI

Применяйте настройки через запущенный Manager, а не через его базу данных:

```powershell
llwmctl settings set --set showOverviewHardware=false --set showModelsHuggingFace=true
llwmctl settings get
```

Поля представления: `showOverviewModelStatus`,
`showOverviewHardware`, `showOverviewSlots`, `showOverviewTokens`,
`showOverviewMtpTokens`, `showOverviewKvCache`,
`showOverviewLiveRuntimeLog`, `showOverviewAllMetrics` и
`showModelsHuggingFace`. Они применяются автоматически и не отключают
базовую телеметрию, журналы или загрузки.

## Операции с последствиями

Полный реестр действий включает установку/сборку/удаление runtime, настройку Windows
и WSL, обслуживание кэша/журналов/истории, управление gateway, обновления, навигацию,
обновление (refresh) и завершение работы.

```powershell
llwmctl operations run <operation> --dry-run --set name=value
llwmctl operations run <operation> --confirm --set name=value
```

Используйте `--confirm` только для операции, живая схема которой помечает
`requiresConfirmation` и последствие которой санкционировал пользователь. Удаление модели
также требует явного намерения и `models delete <model> --confirm`. Удаление модели,
принадлежащей приложению, удаляет её управляемую папку; импортированные модели
по умолчанию только регистрируются (registration-only).

## Перезапуск и восстановление

Перед санкционированным обновлением, завершением работы, удалением runtime или
самоостановкой соберите `status`, `self`, `sessions list` и соответствующие действующие
профиль/настройки. Сообщите состояние и команду восстановления перед выдачей
останавливающего действия. Считайте самоостанавливающую команду финальным действием,
если только независимый контроллер не может наблюдать перезапуск.

После перезапуска перечитайте восстановленный `AGENTS.md`, затем выполните `status`,
`capabilities`, `operations list` и `self`. Сравните сеансы и профили со снимком
до перезапуска. Перезагружайте только сеансы, включённые в запрос.

Сборки release встраивают и восстанавливают соответствующий `llwmctl.exe`, этот файл,
`agent.md` и `docs/CONTROL_API.md`. Чтобы проверить эти сопутствующие файлы
без открытия UI:

```powershell
LlamaCppWindowsManager.exe --bootstrap-agent-sidecars-only
```

## Работа из GitHub или из исходного кода

Для установки конечным пользователем предпочитайте установщик или переносимый ZIP из
[GitHub Releases](https://github.com/MRafStudio/llama-cpp-windows-manager/releases/latest)
и проверяйте соответствующий файл `.sha256`. Не описывайте неподписанный артефакт
как доверенный или подписанный.

```powershell
$asset = "LlamaCppWindowsManager-win-x64.zip"
$expected = ((Get-Content "$asset.sha256" -Raw).Trim() -split "\s+")[0]
$actual = (Get-FileHash $asset -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "Release checksum mismatch: $asset" }
```

Канонический исходный репозиторий —
[github.com/MRafStudio/llama-cpp-windows-manager](https://github.com/MRafStudio/llama-cpp-windows-manager).

Для изменений в репозитории прочитайте `docs/DEVELOPMENT.md`, а для архитектурной
работы — `docs/ARCHITECTURE.md`. Сохраняйте существующие изменения рабочего дерева
и границы сгенерированных данных. Запускайте тесты, соразмерные изменению, а для работы
над управлением, архитектурой, упаковкой или release — полный шлюз (full gate):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-app.ps1 -Restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-gate.ps1
```

Перед запуском сборки из исходников проверьте `llwmctl status`; она не может работать
рядом с production Manager в том же сеансе пользователя. Используйте изолированное
игнорируемое рабочее пространство, никогда не production-данные:

```powershell
$developmentWorkspace = Join-Path $PWD "workspace/development"
$env:LLAMA_CPP_WINDOWS_MANAGER_WORKSPACE = $developmentWorkspace
Start-Process -FilePath .\src\LocalLlmConsole.App\bin\Release\net10.0-windows\win-x64\LlamaCppWindowsManager.exe -WorkingDirectory $PWD
```

Локальные сборки и пакеты не подписаны, если подпись явно не настроена.
Не перезаписывайте и не перезапускайте работающую production-установку только ради
проверки изменения исходников.

## Устранение неполадок

- Запустите `llwmctl help` для синтаксиса и проверяйте живые схемы вместо предположений.
- При сбое команды сохраните возвращённый JSON и код выхода, затем проверьте
  `logs list`, `logs tail` или соответствующий журнал сеанса.
- Для настройки Windows/WSL различайте статус **Started** (запущено) и завершённую
  установку и проверяйте соответствующую операцию статуса после этого.
- При расхождении версий используйте `llwmctl.exe`, восстановленный рядом с этим
  конкретным исполняемым файлом приложения.
- См. [docs/CONTROL_API.md](docs/CONTROL_API.md) для контрактов запросов и деталей
  маршрутов.
