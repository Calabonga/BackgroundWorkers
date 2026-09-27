## Правила архитектуры

- Библиотека расширяется только через наследование: `HostedServiceBase` → `ScopedHostedServiceBase` → `ScheduledHostedServiceBase`. Потребитель наследуется от одного из них и регистрирует воркер через `services.AddHostedService<T>()`. Методов расширения для DI в библиотеке нет — не добавляй их без согласования.
- Все `public` и `protected` члены базовых классов — публичный контракт пакета. Переименование, удаление, смена сигнатуры конструктора или абстрактного члена — ломающее изменение: только с согласованием и повышением major-версии.
- Новые настройки поведения добавляй как `protected virtual` свойства со значением по умолчанию, сохраняющим текущее поведение (как `IncludingSeconds`, `IsDelayBeforeStart`). Новые `abstract` члены ломают всех наследников.
- Целевая платформа — `netstandard2.1`. Не меняй `TargetFramework` и не добавляй `TargetFrameworks` без согласования.
- Зависимости ограничены `Microsoft.Extensions.*.Abstractions` и `ncrontab`. Новые пакеты (в т.ч. `Microsoft.Bcl.TimeProvider`, полный `Microsoft.Extensions.Hosting`) — только после согласования.
- Проект не использует `Calabonga.AspNetCore.AppDefinitions`, Mediator/MediatR, EF Core и `Calabonga.Results` — не вводи их.
- Автоматических тестов нет, поэтому поведение проверяй сборкой и запуском потребителя (пример — репозиторий `Calabonga/BackgroundWorker`).
