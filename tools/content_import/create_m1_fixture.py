"""Create the deterministic synthetic M1-06 course ZIP used by API/browser tests."""
import argparse
import base64
from pathlib import Path
import zipfile


PNG = base64.b64decode("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")


def document(schema, fields, body):
    front = "\n".join([f"schema: {schema}", *[f"{key}: {value}" for key, value in fields]])
    return f"---\n{front}\n---\n\n{body}\n".encode()


def entries(version=1, invalid=False):
    files = {}
    files["course.md"] = document("course/v1.1", [("id", "euler-olympiad"), ("title", "Подготовка к олимпиаде Эйлера")],
        "Курс по олимпиадной математике. Формула Эйлера: $V-E+F=2$. ![Схема](./assets/student/images/euler.png)")
    lessons = [("number-theory", "Остатки и сравнения", 1), ("combinatorics", "Комбинаторные конструкции", 2)]
    for lesson_id, title, order in lessons:
        files[f"lessons/{lesson_id}/lesson.md"] = document("lesson/v1.1", [("id", lesson_id), ("title", title), ("order", order)],
            f"Теория занятия {order}: $a \\equiv b \\pmod m$." +
            ("\n\n![Схема сравнения](../../assets/student/images/euler.png)" if lesson_id == "number-theory" else ""))
    for order in range(1, 6):
        problem_id = f"congruence-{order}"
        files[f"lessons/number-theory/problems/{problem_id}.md"] = document("problem/v1.1",
            [("id", problem_id), ("title", f"Задача {order}"), ("order", order), ("source", "Synthetic M1-06")],
            f"Докажите утверждение для $n={order}$ и вычислите остаток по модулю ${order + 2}$.")
        files[f"materials/hints/{problem_id}.md"] = document("hints/v1",
            [("id", f"hint-{problem_id}"), ("targetKind", "problem"), ("targetId", problem_id), ("order", 1), ("publication", "with-problem")],
            "Рассмотрите классы вычетов и небольшой пример.")
        files[f"materials/teacher/{problem_id}-solution.md"] = document("teacher/v1",
            [("id", f"solution-{problem_id}"), ("targetKind", "problem"), ("targetId", problem_id), ("kind", "solution"), ("order", 1)],
            f"M1_TEACHER_ONLY_SOLUTION_v{version}: полное решение задачи {order}.")
        files[f"materials/teacher/{problem_id}-rubric.md"] = document("teacher/v1",
            [("id", f"rubric-{problem_id}"), ("targetKind", "problem"), ("targetId", problem_id), ("kind", "rubric"), ("order", 2)],
            "M1_TEACHER_ONLY_RUBRIC: проверены идея, выкладки и ответ.")
        if order == 1:
            files[f"materials/teacher/{problem_id}-notes.md"] = document("teacher/v1",
                [("id", f"notes-{problem_id}"), ("targetKind", "problem"), ("targetId", problem_id), ("kind", "notes"), ("order", 3)],
                "M1_TEACHER_ONLY_NOTES: обсудите альтернативный ход после самостоятельной попытки.")
    files["lessons/combinatorics/problems/invariant.md"] = document("problem/v1.1",
        [("id", "invariant"), ("title", "Инвариант раскраски"), ("order", 1), ("source", "Synthetic M1-06")],
        "Найдите инвариант и обоснуйте невозможность конфигурации.")
    files["materials/hints/invariant.md"] = document("hints/v1",
        [("id", "hint-invariant"), ("targetKind", "problem"), ("targetId", "invariant"), ("order", 1), ("publication", "withheld")],
        "M1_TEACHER_ONLY_HINT: исследуйте чётность.")
    files["assets/student/images/euler.png"] = PNG
    document_paths = sorted(path for path in files if path.endswith(".md"))
    inventory = ["schema: content-package/v1", "courseId: euler-olympiad", "documents:"]
    inventory += [f"  - {path}" for path in document_paths]
    inventory += ["assets:", "  - id: euler-diagram", "    path: assets/student/images/euler.png",
        "    ownerKind: course", "    ownerId: euler-olympiad", "    visibility: student", "    mediaType: image/png"]
    files["content-package.yaml"] = ("\n".join(inventory) + "\n").encode()
    if invalid:
        files["lessons/number-theory/problems/congruence-1.md"] = files["lessons/number-theory/problems/congruence-1.md"].replace(b"id: congruence-1\n", b"")
    return files


def write_zip(target, version=1, invalid=False):
    target.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(entries(version, invalid).items()):
            info = zipfile.ZipInfo(name, (2026, 10, 13, 0, 0, 0)); info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3; info.external_attr = 0o100600 << 16; info.extra = b""; info.comment = b""
            archive.writestr(info, data)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(); parser.add_argument("target", type=Path); parser.add_argument("--version", type=int, default=1); parser.add_argument("--invalid", action="store_true")
    args = parser.parse_args(); write_zip(args.target, args.version, args.invalid)
