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
  **`LocalLlmConsole.Service.csproj` (Version)**,
  `MainWindow.xaml` (Title), `installer/LlamaCppWindowsManager.iss` (AppVersion),
  `MainWindow.State.cs` (AppVersionLabel). Забытая константа = неверный бейдж версии!
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
