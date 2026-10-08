# L0.2 — LearningSpace boundary

**PROPOSED / DECISION REQUIRED ORG-G01.** Ничего не реализовано. [Organization](ORGANIZATION-MODEL.md) задаёт организатора, [ProcessingContext](PROCESSING-CONTEXT-MODEL.md) — правовую цель; эти понятия не взаимозаменяемы.

| Вариант | Преимущества | Цена / когда нужен |
|---|---|---|
| A: OrganizationId — учебная граница | Одна граница membership/course/group/files, меньше сущностей и неоднозначностей; подходит внешней организации, ООО и нескольким независимым организациям | Внутри одной организации нет отдельных пространств с независимым lifecycle; группы/курсы ограничиваются grants |
| B: Organization → LearningSpace 1:N | Независимые кампусы/программы/команды и политики внутри одного организатора | Дополнительные ID, membership, provisioning, FK, выбор пространства; таких требований в пилоте нет |
| C: единственный неявный tenant в конфигурации | Минимум schema | Нельзя доказать изоляцию при втором организаторе; отвергнут |

Рекомендуется A. LearningSpace остаётся понятием «изолированный учебный контекст организации», не классом/таблицей/вторым UUID. Для пилота LearningSpace = organizational educational boundary. Различия purpose/retention по курсам решаются версиями policy, а не выдуманными юридическими лицами/пространствами. Организация не даёт всем своим преподавателям доступ ко всем курсам.

Граница охватывает course offering, локальные GroupId, enrollment, teacher grants, assignments и весь граф work/history/annotations/attachments. GroupId имеет смысл только вместе с CourseId; одинаковое имя группы в A/B ничего не связывает. Прогресс/очередь и их totals вычисляются внутри того же scope. Общая библиотека неперсонального контента — отдельное возможное расширение, не право на чужую учебную историю.

В будущем B оправдан при подтверждённом требовании нескольких независимо администрируемых пространств одной организации. Тогда добавить LearningSpace registry и 1:1 default space для каждого проверенного Organization, привязать существующие course roots к нему, разделить membership/grants по необходимости. Дочерние work/history продолжают разрешать scope через корень, их UUID и content refs не меняются. Перевод курса между spaces с историей требует отдельного reviewed процесса; между операторами — ещё и правового основания. Это не обещание миграции без проверок и не часть L1.

Серверный resolver возвращает канонический scope из курса/assignment/parent file и текущего membership. Он не принимает клиентский OrganizationId/LearningSpaceId за полномочие. В single-scope пилоте контекст выбирается сервером; при неоднозначности отказ, а не первый доступный scope. [Isolation](CROSS-ORGANIZATION-ISOLATION.md), [ADR-L12](../adr/ADR-L12-learning-space-boundary.md).
