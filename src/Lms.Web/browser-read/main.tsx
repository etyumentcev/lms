// Test-only entry, excluded from the normal index.html production build.
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { App } from '../src/App';
import '../src/styles/tokens.css';
import '../src/styles/ui.css';
createRoot(document.getElementById('root')!).render(<StrictMode><BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><App contentReads /></BrowserRouter></StrictMode>);
