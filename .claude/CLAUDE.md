# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Правила проекта

- @rules/architecture.md — расширение через наследование, публичный контракт, ограничения зависимостей и платформы.
- @rules/code-styles.md — стиль C# в рамках C# 10.0 / `netstandard2.1`, асинхронность, ошибки, логирование, время.
- @rules/conventions.md — именование классов, файлов, пространств имён, исключений и точек расширения.
- @rules/testing.md — xUnit v3 на Microsoft.Testing.Platform, тестовые наследники, медленные тесты.
- @rules/workflow.md — ветки от `master`, формат коммитов, релиз через push в `master`.

## Обзор

`Calabonga.Microservices.BackgroundWorkers` — небольшая NuGet-библиотека с базовыми классами фоновых
сервисов (`IHostedService`) для приложений ASP.NET Core: воркер с DI-scope на каждую итерацию и воркер
по расписанию CronTab (пакет `ncrontab`). В репозитории — библиотека и unit-тесты, без примера приложения
(пример использования живёт в отдельном репозитории `Calabonga/BackgroundWorker`).
Целевая платформа — `netstandard2.1`, `LangVersion` 10.0.

## Команды

CI собирает `.csproj`, а не решение `src/Calabonga.Microservices.BackgroundWorkers.sln`:

```bash
dotnet restore src/Calabonga.Microservices.BackgroundWorkers/Calabonga.Microservices.BackgroundWorkers.csproj
dotnet build src/Calabonga.Microservices.BackgroundWorkers/Calabonga.Microservices.BackgroundWorkers.csproj --configuration Release --no-restore
dotnet pack src/Calabonga.Microservices.BackgroundWorkers/Calabonga.Microservices.BackgroundWorkers.csproj --configuration Release
```

Тесты идут через Microsoft.Testing.Platform (`global.json`), поэтому проект передаётся через `--project`, а
аргументы xUnit — после `--`:

```bash
dotnet test --project src/Calabonga.Microservices.BackgroundWorkers.Tests/Calabonga.Microservices.BackgroundWorkers.Tests.csproj -c Release
dotnet test --project src/Calabonga.Microservices.BackgroundWorkers.Tests/Calabonga.Microservices.BackgroundWorkers.Tests.csproj -c Release -- --filter-method "*ScheduleEmpty"
```

`GeneratePackageOnBuild` установлен в `true`, поэтому `.nupkg` появляется уже при сборке. CI
(`.github/workflows/main.yml`) запускается при push в `master` и вручную (`workflow_dispatch`): сборка на
`windows-latest` с .NET SDK 10.0.x, `dotnet test`, `dotnet pack` и `dotnet nuget push` на nuget.org с секретом
`NUGET_API_KEY`. Флага `--skip-duplicate` нет — push без изменения `<Version>` в `.csproj` уронит CI.
При выпуске поднимается `<Version>` и обновляется `<PackageReleaseNotes>`. `README.md` и `logo.png`
упаковываются в пакет.

## Архитектура

Три уровня наследования в `src/Calabonga.Microservices.BackgroundWorkers/`:

- **`Base/HostedServiceBase.cs`** — `HostedServiceBase : IHostedService`, собственная реализация (не
  `BackgroundService` из Microsoft.Extensions.Hosting). `StartAsync` запускает `ExecuteAsync` на внутреннем
  `CancellationTokenSource`; `StopAsync` отменяет его и ждёт завершения либо отмены токена остановки.
  `ExecuteAsync` по умолчанию: `ProcessAsync` → `TimeProvider.Delay(5 с)` в цикле. Абстрактный метод — `ProcessAsync`.
  Свойство `TimeProvider` (protected) — источник текущего времени и задержек.
- **`ScopedBackgroundHostedService.cs`** — класс `ScopedHostedServiceBase : HostedServiceBase`. Принимает
  `IServiceScopeFactory`, `ILogger` и опционально `TimeProvider`, на каждый вызов `ProcessAsync` создаёт scope и вызывает абстрактный
  `ProcessInScopeAsync(IServiceProvider, CancellationToken)`. Свойства `ServiceName`, `Logger`.
- **`ScheduledBackgroundHostedService.cs`** — класс `ScheduledHostedServiceBase : ScopedHostedServiceBase`.
  Наследник задаёт `Schedule` (cron-строка) и `DisplayName`; опционально переопределяет `IncludingSeconds`
  (6-польный формат с секундами), `IsExecuteOnServerRestart` (первый запуск через 5 с после старта),
  `IsDelayBeforeStart`. Публичное свойство `NextRun` — время следующего запуска.
- **`Exceptions/WorkerArgumentNullException.cs`** — бросается, если `Schedule` пустой.

У каждого базового класса две перегрузки конструктора: без `TimeProvider` (используется `TimeProvider.System`) и
с ним (с 3.1.0, пакет `Microsoft.Bcl.TimeProvider`). Перегрузка без `TimeProvider` — для совместимости со
старыми наследниками, её нельзя удалять.

Тесты — `src/Calabonga.Microservices.BackgroundWorkers.Tests/`. Время в них управляется `SteppingTimeProvider`
(обёртка над `FakeTimeProvider`, считает созданные таймеры): `AdvanceToNextIterationAsync` сдвигает время и ждёт,
пока цикл закончит итерацию и заведёт следующий таймер.

### Что важно знать

- Имена файлов не совпадают с именами классов: `ScopedBackgroundHostedService.cs` → `ScopedHostedServiceBase`,
  `ScheduledBackgroundHostedService.cs` → `ScheduledHostedServiceBase`. Искать по имени класса, не файла.
- `netstandard2.1` + C# 10.0: file-scoped namespaces и `ImplicitUsings` есть, но `record`/`init` недоступны
  (нет `IsExternalInit`). Общие правила из `C:\Projects\.claude\rules\` тоже загружаются; при противоречии приоритет у `@rules/` этого репозитория.
- `ScheduledHostedServiceBase` вызывает `GetSchedule()` из конструктора, а тот читает виртуальные/абстрактные
  члены (`Schedule`, `IncludingSeconds`, `IsExecuteOnServerRestart`, `DisplayName`). Их переопределения в
  наследнике выполняются до конструктора наследника — они не должны зависеть от его полей.
- `HostedServiceBase.StartAsync` вызывает `ExecuteAsync` синхронно до первого незавершённого `await`. Каждая
  итерация цикла в `ExecuteAsync` обязана содержать `await TimeProvider.Delay(...)`: цикл без него не только грузит ядро
  на 100%, но и не даёт `StartAsync` вернуться — хост не стартует (так было при `IsDelayBeforeStart = false`
  до 3.0.1).
- `IsDelayBeforeStart` управляет только 5-секундной задержкой перед первой проверкой расписания; пауза между
  итерациями есть всегда.
- Проверка расписания идёт раз в 5 с, поэтому точность срабатывания ±5 с; расписания с интервалом меньше
  5 с не работают как ожидается. Пропущенные запуски не догоняются — `NextRun` пересчитывается от текущего
  времени.
- Время — `TimeProvider.GetUtcNow().UtcDateTime`: cron-расписание и `NextRun` в UTC, независимо от часового
  пояса сервера (с версии 3.0.0; до неё было локальное `DateTime.Now`). Прямые `DateTime.UtcNow`/`Task.Delay(...)`
  в библиотеке не используются — иначе тесты на `FakeTimeProvider` перестанут управлять временем. Исключение —
  `Task.Delay(Timeout.Infinite, cancellationToken)` в `StopAsync`: он ждёт токен остановки хоста, а не время.
- `ScopedHostedServiceBase.ProcessAsync` ловит все исключения, логирует и продолжает работу; наружу уходит
  только `OperationCanceledException` при отменённом токене. Падение одной итерации не останавливает воркер.
- `HostedServiceBase` не реализует `IDisposable` — внутренний `CancellationTokenSource` не освобождается.
- Изменения в `protected`/`public` членах базовых классов — ломающие для потребителей пакета (наследников).
