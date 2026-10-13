import Markdown from 'react-markdown';
import remarkMath from 'remark-math';
import rehypeKatex from 'rehype-katex';
import { useEffect, useState } from 'react';
import { createSessionObjectUrl, revokeSessionObjectUrl } from '../api/sessionResources';
import 'katex/dist/katex.min.css';
import './markdown.css';

export type MarkdownAsset = { artifactPath: string; mediaType: string };
type AssetLoader = (artifactPath: string) => Promise<Blob>;

function ProtectedAsset({ path, alt, image, load }: { path: string; alt?: string; image: boolean; load: AssetLoader }) {
  const [url, setUrl] = useState<string>();
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let active = true, local: string | undefined;
    void load(path).then(blob => {
      if (!active) return;
      local = createSessionObjectUrl(blob); setUrl(local);
    }).catch(() => { if (active) setFailed(true); });
    return () => { active = false; if (local) revokeSessionObjectUrl(local); };
  }, [path, load]);
  if (failed) return <span className="markdown-asset-error">{alt || 'Вложение недоступно'}</span>;
  if (!url) return <span className="markdown-asset-loading">Загрузка {image ? 'изображения' : 'вложения'}…</span>;
  return image ? <img className="markdown-protected-image" src={url} alt={alt || ''} />
    : <a href={url} download>{alt || 'Скачать вложение'}</a>;
}

/** Raw HTML is never parsed. Remote images and unlisted local assets never load. TeX is untrusted. */
export function MarkdownContent({ children, assets = [], loadAsset }: {
  children: string; assets?: readonly MarkdownAsset[]; loadAsset?: AssetLoader;
}) {
  const manifest = new Map(assets.map(asset => [asset.artifactPath, asset]));
  const local = (href?: string) => href?.startsWith('./assets/') ? href.slice(2) : undefined;
  return <div className="markdown-content"><Markdown skipHtml remarkPlugins={[remarkMath]}
    rehypePlugins={[[rehypeKatex, { trust: false, strict: 'ignore', maxExpand: 1000 }]]}
    components={{ h1: ({ children }) => <h2>{children}</h2>,
      img: ({ alt, src }) => { const path = local(src); const asset = path ? manifest.get(path) : undefined;
        return path && asset?.mediaType.startsWith('image/') && loadAsset
          ? <ProtectedAsset path={path} alt={alt ?? undefined} image load={loadAsset} /> : <span>{alt}</span>; },
      a: ({ children, href }) => { const path = local(href); const asset = path ? manifest.get(path) : undefined;
        if (path) return asset && loadAsset ? <ProtectedAsset path={path} alt={String(children)} image={false} load={loadAsset} /> : <span>{children}</span>;
        try { const url = new URL(href ?? ''); if (url.protocol === 'https:') return <a href={url.href} rel="noopener noreferrer">{children}</a>; }
        catch { /* Unsafe or relative non-asset links remain text. */ }
        return <span>{children}</span>; } }}>
    {children}
  </Markdown></div>;
}
