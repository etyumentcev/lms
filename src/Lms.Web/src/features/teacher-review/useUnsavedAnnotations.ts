import { useContext, useEffect } from 'react';
import { UNSAFE_NavigationContext } from 'react-router-dom';
const warning = 'Графические пометки ещё не сохранены. Уйти и потерять пометки?';
/** BrowserRouter uses this navigator for all SPA links, including programmatic navigation. */
export function useUnsavedAnnotations(dirty: boolean) {
  const { navigator } = useContext(UNSAFE_NavigationContext);
  useEffect(() => {
    if (!dirty) return;
    const before = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = ''; };
    const push = navigator.push, replace = navigator.replace;
    navigator.push = (...args) => { if (window.confirm(warning)) push.apply(navigator, args); };
    navigator.replace = (...args) => { if (window.confirm(warning)) replace.apply(navigator, args); };
    const index = window.history.state?.idx;
    let restoring = false;
    const pop = (e: PopStateEvent) => {
      if (restoring) { restoring = false; e.stopImmediatePropagation(); return; }
      const next = e.state?.idx;
      if (typeof index === 'number' && typeof next === 'number' && next !== index && !window.confirm(warning)) {
        e.stopImmediatePropagation(); restoring = true; window.history.go(index - next);
      }
    };
    window.addEventListener('beforeunload', before);
    window.addEventListener('popstate', pop, true);
    return () => { navigator.push = push; navigator.replace = replace; window.removeEventListener('beforeunload', before); window.removeEventListener('popstate', pop, true); };
  }, [dirty, navigator]);
}
