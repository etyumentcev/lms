import Markdown from 'react-markdown';
import remarkMath from 'remark-math';
import rehypeKatex from 'rehype-katex';
import 'katex/dist/katex.min.css';
import './markdown.css';

/** Raw HTML is never parsed. No remote images or trusted TeX commands. */
export function MarkdownContent({ children }: { children: string }) {
  return <div className="markdown-content"><Markdown skipHtml remarkPlugins={[remarkMath]}
    rehypePlugins={[[rehypeKatex, { trust: false, strict: 'ignore', maxExpand: 1000 }]]}
    components={{ h1: ({ children }) => <h2>{children}</h2>, img: ({ alt }) => <span>{alt}</span> }}>
    {children}
  </Markdown></div>;
}
