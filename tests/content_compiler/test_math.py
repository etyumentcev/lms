import json
import os
import subprocess
import unittest
from unittest.mock import patch
import test_compiler as fixtures
from test_compiler import PROBLEM, SOLUTION


class MathTests(unittest.TestCase):
    setUp=fixtures.CompilerTests.setUp
    build=fixtures.CompilerTests.build
    body=fixtures.CompilerTests.body
    denied=fixtures.CompilerTests.denied
    valid=fixtures.CompilerTests.valid

    def test_inline_display_arrays_and_verbatim_preservation(self):
        body=r'''Inline $a \mid b$ and $\frac{a^2}{b}$.

$$
\begin{aligned}
a &= b+c \\
d &= \sqrt{4}
\end{aligned}
$$

> $$x^2 + y^2 = z^2$$
'''
        self.body(PROBLEM,body)
        value=json.loads(self.valid()['studentArtifact']['json'])['course']['lessons'][0]['problems'][0]['bodyMarkdown']
        self.assertEqual(body,value)

    def test_syntax_unknown_commands_and_unclosed_delimiters(self):
        for text in [r'$\frac{a}{$',r'$\unknowncommand{x}$','$x+1','$$\nx+1\n',r'$\begin{matrix}1\end{aligned}$','$$ $$']:
            with self.subTest(text=text):self.body(PROBLEM,text+'\n');self.denied('E_MATH')

    def test_untrusted_commands_never_admitted(self):
        for text in [r'$\href{https://example.test}{x}$',r'$\includegraphics{file:///secret}$',r'$\htmlStyle{color:red}{x}$',r'$\url{https://example.test}$']:
            with self.subTest(text=text):self.body(PROBLEM,text+'\n');self.denied('E_MATH')

    def test_math_has_no_markdown_dependencies(self):
        body=r'''$\text{[x](missing.pdf)}$

$$
\text{[reference]: secret.txt}
$$
'''
        self.body(PROBLEM,body);r=self.valid()
        self.assertEqual(1,len(r['studentArtifact']['assets']))
        self.assertIn(b'missing.pdf',r['studentArtifact']['json'])

    def test_code_is_not_math_and_escaped_dollars(self):
        body='`$\\unknown$`\n\n```tex\n$\\unknown$\n$$\n```\n\nPrice: \\$5.\n'
        self.body(PROBLEM,body);r=self.valid()
        self.assertEqual(body,json.loads(r['studentArtifact']['json'])['course']['lessons'][0]['problems'][0]['bodyMarkdown'])

    def test_closed_material_failure_is_atomic_and_coordinates_are_unicode(self):
        self.body(SOLUTION,'αβ $\\unknown$\n')
        d=next(d for d in self.denied('E_MATH')['diagnostics'] if d['code']=='E_MATH')
        self.assertEqual(SOLUTION,d['file']);self.assertEqual(4,d['column'])

    def test_macros_are_local_and_expansion_is_bounded(self):
        self.body(PROBLEM,r'$\def\localmacro{x}\localmacro$'+'\n');self.valid()
        self.body(PROBLEM,r'$\def\localmacro{x}\localmacro$ then $\localmacro$'+'\n');self.denied('E_MATH')
        self.body(PROBLEM,r'$\def\loop{\loop}\loop$'+'\n');self.denied('E_MATH')

    def test_runtime_configuration_and_failures_are_closed(self):
        with patch.dict(os.environ,{'SAPIENTUM_CONTENT_NODE':'/missing/node'}), patch('tools.content_compiler.snapshot.Snapshot.__enter__',side_effect=AssertionError('must not read input')):
            self.denied('E_CONFIG')
        with patch('tools.content_compiler.math_validation.subprocess.run',side_effect=subprocess.TimeoutExpired('node',30)):
            self.denied('E_INTERNAL')
        with patch('tools.content_compiler.math_validation.subprocess.run',return_value=subprocess.CompletedProcess([],2,b'',b'')):
            self.denied('E_CONFIG')
        with patch('tools.content_compiler.math_validation.subprocess.run',return_value=subprocess.CompletedProcess([],0,b'[]',b'')):
            self.denied('E_INTERNAL')

if __name__=='__main__':unittest.main()
