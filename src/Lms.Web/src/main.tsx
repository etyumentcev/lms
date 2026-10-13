import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { App } from './App';
import { SessionGate, SessionProvider } from './auth/session';
import './styles/tokens.css';
import './styles/ui.css';
createRoot(document.getElementById('root')!).render(
  <StrictMode><SessionProvider><SessionGate><BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><App /></BrowserRouter></SessionGate></SessionProvider></StrictMode>,
);
