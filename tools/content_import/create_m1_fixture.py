"""Create the deterministic synthetic M1-06 course ZIP used by API/browser tests."""
import argparse
import base64
from pathlib import Path
import zipfile


PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")


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
            f"Теория занятия {order}: $a \\equiv b \\pmod m$.")
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
