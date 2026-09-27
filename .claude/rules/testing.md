## Договоренности по тестированию

- Тесты: `src/Calabonga.Microservices.BackgroundWorkers.Tests/` (`net10.0`, в `src/Calabonga.Microservices.BackgroundWorkers.sln`). Сама библиотека остаётся `netstandard2.1`.
- Frameworks: xUnit v3 (`xunit.v3`), Moq, AutoFixture (`AutoFixture.AutoMoq`).
- Раннер — Microsoft.Testing.Platform (включён в `global.json` в корне репозитория). Пакеты VSTest (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) не добавляй: на .NET 10 SDK `dotnet test` в режиме VSTest с xUnit v3 падает.
- CI запускает `dotnet test` перед `dotnet pack` — упавший тест блокирует публикацию пакета.

### Как писать тесты
- Тестируй базовые классы через тестовых наследников, вложенных `private sealed` классами в тестовый класс; protected-члены открывай через публичные обёртки (`RunProcessAsync`).
- Настройки `ScheduledHostedServiceBase` (`Schedule`, `IncludingSeconds`, `IsExecuteOnServerRestart`, `IsDelayBeforeStart`) читаются в базовом конструкторе, поэтому передавать их через конструктор наследника нельзя — на каждый сценарий отдельный наследник с константными override.
- `PeriodicHostedServiceBase` читает настройки при старте, поэтому для него достаточно одного тестового наследника с настройками через конструктор. Случайные интервалы — через наследника `Random` с заданной последовательностью (`SequenceRandom`), а проверки диапазона на настоящем `Random` — только с фиксированным seed, чтобы тест не был вероятностным.
- `IServiceScopeFactory`, `IServiceScope`, `ILogger` — через Moq; проверка логов — `LoggerMockExtensions.VerifyLog`.
- Используй `TestContext.Current.CancellationToken` в тестах.
- Время — только через `TimeProvider`-перегрузки конструкторов: `SteppingTimeProvider` для тестов цикла, `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) для тестов конструктора. Тесты не должны ждать реальные 5 секунд и зависеть от текущего времени.
- Цикл продвигай через `SteppingTimeProvider.AdvanceToNextIterationAsync`: он ждёт, пока итерация завершится и цикл заведёт следующий таймер. Не делай `Advance` + `Task.Delay(...)` — следующий таймер может быть создан уже после сдвига времени, и тест станет нестабильным. Один сдвиг запускает не больше одной итерации.
- `SteppingTimeProvider.Advance` (без ожидания) — только когда ни один таймер не должен сработать.
- Тест на зависание (цикл без `await`) запускай через `Task.Run` + `Task.WhenAny` с таймаутом, иначе тестовый поток зависнет вместе с кодом.
- Регрессионный тест на исправленный баг проверяй на коде до исправления — он должен падать.

### Шаблоны для именования
- `MethodName_Should_ExpectedBehavior_When_Condition` (например, `Constructor_Should_ThrowWorkerArgumentNullException_When_ScheduleEmpty`).
- `MethodName_ShouldNot_ExpectedBehavior_When_Condition` (например, `ProcessAsync_ShouldNot_Throw_When_ProcessThrows`).
