#!/usr/bin/env python3
"""Build deterministic synthetic course packages declared by manifest.json."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import zipfile


LESSON_TITLES = [
    "Чётность и разбиения", "Делимость и остатки", "Признаки делимости",
    "Простые числа", "НОД и алгоритм Евклида", "Диофантовы уравнения",
    "Принцип Дирихле", "Инварианты", "Раскраски", "Игры и стратегии",
    "Подсчёт двумя способами", "Перестановки и сочетания", "Графы",
    "Маршруты и связность", "Геометрические построения", "Углы и окружности",
    "Площади", "Подобие", "Неравенства", "Оценки и экстремумы",
    "Последовательности", "Рекуррентные соотношения", "Смешанный практикум",
    "Итоговая олимпиада",
]


def document(schema: str, fields: list[tuple[str, object]], body: str) -> bytes:
    front = "\n".join([f"schema: {schema}", *[f"{key}: {value}" for key, value in fields]])
    return f"---\n{front}\n---\n\n{body}\n".encode()


def entries(course: dict) -> dict[str, bytes]:
    course_id = course["key"]
    files: dict[str, bytes] = {
        "course.md": document(
            "course/v1.1",
            [("id", course_id), ("title", course["title"])],
            course["description"] + "\n\nВсе данные и участники этого курса синтетические.",
        )
    }
    for lesson_no in range(1, course["lessonCount"] + 1):
        lesson_id = f"lesson-{lesson_no:02d}"
        title = LESSON_TITLES[lesson_no - 1]
        files[f"lessons/{lesson_id}/lesson.md"] = document(
            "lesson/v1.1",
            [("id", lesson_id), ("title", title), ("order", lesson_no)],
            f"Теория и разбор примеров по теме «{title}». Формула занятия: $a \\equiv b \\pmod m$.",
        )
        problem_count = 5 if lesson_no == 1 else 1
        for problem_no in range(1, problem_count + 1):
            problem_id = f"{lesson_id}-problem-{problem_no}"
            files[f"lessons/{lesson_id}/problems/{problem_id}.md"] = document(
                "problem/v1.1",
                [("id", problem_id), ("title", f"{title} · задача {problem_no}"),
                 ("order", problem_no), ("source", "Synthetic pilot fixture")],
                f"Докажите утверждение для $n={lesson_no + problem_no}$ и подробно объясните ход решения.",
            )
            files[f"materials/hints/{problem_id}.md"] = document(
                "hints/v1",
                [("id", f"hint-{problem_id}"), ("targetKind", "problem"),
                 ("targetId", problem_id), ("order", 1), ("publication", "with-problem")],
                "Начните с малого примера и сформулируйте наблюдаемую закономерность.",
            )
            files[f"materials/teacher/{problem_id}-solution.md"] = document(
                "teacher/v1",
                [("id", f"solution-{problem_id}"), ("targetKind", "problem"),
                 ("targetId", problem_id), ("kind", "solution"), ("order", 1)],
                "Синтетическое методическое решение: выделите ключевую идею, затем проверьте крайние случаи.",
            )
            files[f"materials/teacher/{problem_id}-rubric.md"] = document(
                "teacher/v1",
                [("id", f"rubric-{problem_id}"), ("targetKind", "problem"),
                 ("targetId", problem_id), ("kind", "rubric"), ("order", 2)],
                "Критерии: идея, корректность преобразований, полнота обоснования и ясный ответ.",
            )

    documents = sorted(path for path in files if path.endswith(".md"))
    inventory = ["schema: content-package/v1", f"courseId: {course_id}", "documents:"]
    inventory.extend(f"  - {path}" for path in documents)
    inventory.append("assets: []")
    files["content-package.yaml"] = ("\n".join(inventory) + "\n").encode()
    return files


def write_package(course: dict, target: Path) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(entries(course).items()):
            info = zipfile.ZipInfo(name, (2026, 10, 14, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100600 << 16
            archive.writestr(info, data)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text())
    for organization in manifest["organizations"]:
        for course in organization["courses"]:
            write_package(course, args.output / f"{course['key']}.zip")


if __name__ == "__main__":
    main()
