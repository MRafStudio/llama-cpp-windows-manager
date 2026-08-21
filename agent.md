# Быстрый старт автоматизации

Перед работой с llama.cpp Windows Manager прочитайте [AGENTS.md](AGENTS.md).
Это авторитетное руководство по обнаружению, идентификации моделей,
безопасности жизненного цикла, перезапускам, загрузкам, настройкам,
установке и работе с исходным кодом.

Канонический исходный репозиторий:
<https://github.com/MRafStudio/llama-cpp-windows-manager>; для установки
используйте GitHub Releases, а клонируйте репозиторий только для разработки
или ревью.

Используйте `llwmctl.exe`, расположенный рядом с установленным
или portable-приложением:

```powershell
./llwmctl.exe status
./llwmctl.exe capabilities
./llwmctl.exe operations list
./llwmctl.exe self
```

`llwmctl` управляет запущенным Manager'ом через его аутентифицированный
loopback API. Не редактируйте базу данных Manager'а, не запускайте
`llama-server` напрямую, не открывайте доступ к API управления
и не автоматизируйте WPF-интерфейс.

Сначала разрешите идентификаторы:

```powershell
llwmctl models list
llwmctl runtimes list
llwmctl profiles list --model <model>
llwmctl sessions list
```

Предпочитайте сохранённые профили и дожидайтесь готовности:

```powershell
llwmctl load <model> --profile <profile> --wait
llwmctl sessions inspect <session>
llwmctl gateway inspect
llwmctl sessions metrics <session>
llwmctl sessions logs <session>
```

Запускайте `self` перед любым действием, которое может остановить или
заменить загруженную модель. Никогда не используйте `--allow-self-stop`
или `--confirm` без явного разрешения на заявленное последствие. Проверяйте
незнакомые или ответственные операции по живой схеме и через
`operations run <name> --dry-run`.

Если обнаружение неоднозначно, используйте `--workspace <path>` или
`--connection <workspace>\state\control.json`. Если Manager не запущен,
а пользователь попросил им управлять, запустите `LlamaCppWindowsManager.exe`
обычным образом и повторите `status`; никогда не запускайте второй экземпляр.

Релизные сборки восстанавливают соответствующие CLI- и операторские
документации рядом с исполняемым файлом приложения. Проверьте это без запуска
UI командой:

```powershell
LlamaCppWindowsManager.exe --bootstrap-agent-sidecars-only
```

Полные контракты команд и HTTP см. в
[Локальный API управления и `llwmctl`](docs/CONTROL_API.md).
