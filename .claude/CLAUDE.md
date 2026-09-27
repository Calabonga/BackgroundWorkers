# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Правила проекта

- @rules/architecture.md — расширение через наследование, публичный контракт, ограничения зависимостей и платформы.
- @rules/code-styles.md — стиль C# в рамках C# 8.0 / `netstandard2.1`, асинхронность, ошибки, логирование, время.
- @rules/conventions.md — именование классов, файлов, пространств имён, исключений и точек расширения.
- @rules/testing.md — тестов нет; как оформлять тестовый проект, если он появится.
- @rules/workflow.md — ветки от `master`, формат коммитов, релиз через push в `master`.

## Обзор

`Calabonga.Microservices.BackgroundWorkers` — небольшая NuGet-библиотека с базовыми классами фоновых
сервисов (`IHostedService`) для приложений ASP.NET Core: воркер с DI-scope на каждую итерацию и воркер
по расписанию CronTab (пакет `ncrontab`). В репозитории находится **только библиотека** — без примера
приложения и без тестов (пример использования живёт в отдельном репозитории `Calabonga/BackgroundWorker`).
Целевая платформа — `netstandard2.1`, `LangVersion` 8.0.

## Команды

CI собирает `.csproj`, а не решение `src/Calabonga.Microservices.BackgroundWorkers.sln`:

```bash
dotnet restore src/Calabonga.Microservices.BackgroundWorkers/Calabonga.Microservices.BackgroundWorkers.csproj
dotnet build src/Calabonga.Microservices.BackgroundWorkers/Calabonga.Microservices.BackgroundWorkers.csproj --configuration Release --no-restore
dotnet pack src/Calabonga.Microservices.BackgroundWorkers/Calabonga.Microservices.BackgroundWorkers.csproj --configuration Release
```

`GeneratePackageOnBuild` установлен в `true`, поэтому `.nupkg` появляется уже при сборке. CI
(`.github/workflows/main.yml`) запускается при push в `master` и вручную (`workflow_dispatch`): сборка на
`windows-latest` с .NET SDK 7.0.x, `dotnet pack` и `dotnet nuget push` на nuget.org с секретом
`NUGET_API_KEY`. Флага `--skip-duplicate` нет — push без изменения `<Version>` в `.csproj` уронит CI.
При выпуске поднимается `<Version>` и обновляется `<PackageReleaseNotes>`. `README.md` и `logo.png`
упаковываются в пакет.

## Архитектура

Три уровня наследования в `src/Calabonga.Microservices.BackgroundWorkers/`:

- **`Base/HostedServiceBase.cs`** — `HostedServiceBase : IHostedService`, собственная реализация (не
  `BackgroundService` из Microsoft.Extensions.Hosting). `StartAsync` запускает `ExecuteAsync` на внутреннем
  `CancellationTokenSource`; `StopAsync` отменяет его и ждёт завершения либо отмены токена остановки.
  `ExecuteAsync` по умолчанию: `ProcessAsync` → `Task.Delay(5000)` в цикле. Абстрактный метод — `ProcessAsync`.
- **`ScopedBackgroundHostedService.cs`** — класс `ScopedHostedServiceBase : HostedServiceBase`. Принимает
  `IServiceScopeFactory` и `ILogger`, на каждый вызов `ProcessAsync` создаёт scope и вызывает абстрактный
  `ProcessInScopeAsync(IServiceProvider, CancellationToken)`. Свойства `ServiceName`, `Logger`.
- **`ScheduledBackgroundHostedService.cs`** — класс `ScheduledHostedServiceBase : ScopedHostedServiceBase`.
  Наследник задаёт `Schedule` (cron-строка) и `DisplayName`; опционально переопределяет `IncludingSeconds`
  (6-польный формат с секундами), `IsExecuteOnServerRestart` (первый запуск через 5 с после старта),
  `IsDelayBeforeStart`. Публичное свойство `NextRun` — время следующего запуска.
- **`Exceptions/WorkerArgumentNullException.cs`** — бросается, если `Schedule` пустой.

### Что важно знать

- Имена файлов не совпадают с именами классов: `ScopedBackgroundHostedService.cs` → `ScopedHostedServiceBase`,
  `ScheduledBackgroundHostedService.cs` → `ScheduledHostedServiceBase`. Искать по имени класса, не файла.
- `netstandard2.1` + C# 8.0: нельзя file-scoped namespaces, `record`, `init`, глобальные using и т.п.
  Общие правила из `C:\Projects\.claude\rules\` тоже загружаются; при противоречии приоритет у `@rules/` этого репозитория.
- `ScheduledHostedServiceBase` вызывает `GetSchedule()` из конструктора, а тот читает виртуальные/абстрактные
  члены (`Schedule`, `IncludingSeconds`, `IsExecuteOnServerRestart`, `DisplayName`). Их переопределения в
  наследнике выполняются до конструктора наследника — они не должны зависеть от его полей.
- `IsDelayBeforeStart` на деле управляет 5-секундной паузой между **всеми** итерациями цикла, а не только
  перед стартом. При `false` цикл крутится без задержки (busy-loop, 100% CPU ядра).
- Проверка расписания идёт раз в 5 с, поэтому точность срабатывания ±5 с; расписания с интервалом меньше
  5 с не работают как ожидается. Пропущенные запуски не догоняются — `NextRun` пересчитывается от текущего
  времени.
- Время — локальное `DateTime.Now`, не UTC; cron-расписание интерпретируется в часовом поясе сервера.
- `ScopedHostedServiceBase.ProcessAsync` ловит все исключения, логирует и продолжает работу; наружу уходит
  только `OperationCanceledException` при отменённом токене. Падение одной итерации не останавливает воркер.
- `HostedServiceBase` не реализует `IDisposable` — внутренний `CancellationTokenSource` не освобождается.
- Изменения в `protected`/`public` членах базовых классов — ломающие для потребителей пакета (наследников).
