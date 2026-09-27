## Договоренности по тестированию

- Тестового проекта в репозитории нет. Не создавай его самостоятельно — уточни, нужен ли он.
- Пока тестов нет, изменения проверяй сборкой `.csproj` в Release и запуском потребителя.

### Если тестовый проект будет создан
- Расположение: `src/Calabonga.Microservices.BackgroundWorkers.Tests/`, добавить в `src/Calabonga.Microservices.BackgroundWorkers.sln`.
- Целевая платформа тестов — актуальная .NET (сама библиотека остаётся `netstandard2.1`).
- Frameworks: xUnit версии 3 и выше, Moq, AutoFixture (`AutoFixture.AutoMoq`).
- Тестируй воркеры через тестовых наследников базовых классов; `IServiceScopeFactory` и `ILogger` — через Moq.
- Для сценариев жизненного цикла (`StartAsync`/`StopAsync`) используй `Host` из `Microsoft.Extensions.Hosting` в тестовом проекте, а не `WebApplicationFactory`.
- Не делай тесты, зависящие от реального ожидания 5-секундного `Task.Delay` и текущего времени, без крайней необходимости.
- Добавь шаг `dotnet test` в `.github/workflows/main.yml` перед `dotnet pack`.

### Шаблоны для именования
- `MethodName_Should_ExpectedBehavior_When_Condition` (например, `Constructor_Should_ThrowWorkerArgumentNullException_When_ScheduleEmpty`).
- `MethodName_ShouldNot_ExpectedBehavior_When_Condition` (например, `ProcessAsync_ShouldNot_Throw_When_ProcessInScopeFails`).
