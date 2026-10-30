import {useEffect, useReducer, useRef, useState} from 'react';
import {createRoot} from 'react-dom/client';
import {ChatStreamError, runChat} from './chatStream';
import {answerReducer, initialAnswer} from './chatState';
import './style.css';

type Lang = 'es' | 'en';
type Branding = {companyName: string; assistantName: string; logoUrl: string; defaultLanguage: Lang};
type SessionUser = {displayName: string; roles: string[]; permissions: string[]};
type Session = {isAuthenticated: boolean; user?: SessionUser};

const copy = {
  es: {
    language: 'Idioma',
    badge: 'Demostración · datos ficticios',
    title: 'Tus políticas, más cerca.',
    intro: 'Consulta vacaciones, beneficios y trabajo remoto en un solo lugar.',
    notice: 'El asistente responde únicamente con las políticas aprobadas y cita sus fuentes.',
    topics: ['Vacaciones', 'Beneficios', 'Teletrabajo'],
    label: 'Tu pregunta',
    placeholder: '¿Cómo solicito vacaciones?',
    button: 'Enviar',
    stop: 'Detener',
    thinking: 'Pensando…',
    provisional: 'Generando respuesta provisional. Aún no verificada; puede contener errores.',
    confirmed: 'Respuesta completada; referencias comprobadas. Esto no garantiza el respaldo de cada afirmación.',
    cancelled: 'Generación cancelada. Se ha retirado el texto provisional.',
    timeout: 'Se agotó el tiempo de espera. Se ha retirado el texto provisional.',
    sourcesLabel: 'Fuentes',
    answerLabel: 'Respuesta del asistente',
    noAnswer: 'No encontré una respuesta en las políticas disponibles. Para este tema, contacta con Recursos Humanos.',
    chatError: 'No se pudo completar la respuesta. Se ha retirado el texto provisional. Inténtalo de nuevo.',
    needLogin: 'Inicia sesión para hacer preguntas.',
    noPermission: 'Tu cuenta no tiene permiso para usar el asistente.',
    error: 'No se pudo cargar la configuración. Se muestra la marca de ejemplo.',
    skip: 'Ir al contenido',
    login: 'Iniciar sesión',
    logout: 'Cerrar sesión',
    sessionError: 'No se pudo comprobar la sesión o preparar el cierre seguro.'
  },
  en: {
    language: 'Language',
    badge: 'Demo · fictional data',
    title: 'Your policies, within reach.',
    intro: 'Find leave, benefits and remote-work policies in one place.',
    notice: 'The assistant answers only from approved policies and cites its sources.',
    topics: ['Annual leave', 'Benefits', 'Remote work'],
    label: 'Your question',
    placeholder: 'How do I request leave?',
    button: 'Send',
    stop: 'Stop',
    thinking: 'Thinking…',
    provisional: 'Generating a provisional answer. Not yet verified; it may contain errors.',
    confirmed: 'Answer completed; references checked. This does not guarantee support for every claim.',
    cancelled: 'Generation cancelled. Provisional text has been removed.',
    timeout: 'The request timed out. Provisional text has been removed.',
    sourcesLabel: 'Sources',
    answerLabel: 'Assistant answer',
    noAnswer: "I couldn't find an answer in the available policies. For this topic, please contact HR.",
    chatError: 'The answer could not be completed. Provisional text has been removed. Please try again.',
    needLogin: 'Sign in to ask questions.',
    noPermission: 'Your account does not have permission to use the assistant.',
    error: 'Configuration could not be loaded. Showing sample branding.',
    skip: 'Skip to content',
    login: 'Sign in',
    logout: 'Sign out',
    sessionError: 'The session could not be checked or secure sign-out could not be prepared.'
  }
};
function readCsrfToken() {
  const prefix = '__Host-CompanyAssistant.XSRF=';
  const cookie = document.cookie.split('; ').find(value => value.startsWith(prefix));
  return cookie ? decodeURIComponent(cookie.slice(prefix.length)) : '';
}

function App() {
  const [lang, setLang] = useState<Lang>('es');
  const [brand, setBrand] = useState<Branding>({
    companyName: 'NovaWorks',
    assistantName: 'Nova Assistant',
    logoUrl: '/logo.svg',
    defaultLanguage: 'es'
  });
  const [error, setError] = useState(false);
  const [session, setSession] = useState<Session | null>(null);
  const [sessionError, setSessionError] = useState(false);
  const [csrfToken, setCsrfToken] = useState('');

  const [message, setMessage] = useState('');
  const [answerState, dispatchAnswer] = useReducer(answerReducer, initialAnswer);
  const {text: answer, citations: sources, phase} = answerState;
  const limited = phase === 'limited';
  const [busy, setBusy] = useState(false);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [chatError, setChatError] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    fetch('/api/branding', {signal: controller.signal})
      .then(r => {
        if (!r.ok) throw new Error();
        return r.json();
      })
      .then((b: Branding) => {
        setBrand(b);
        setLang(b.defaultLanguage === 'en' ? 'en' : 'es');
      })
      .catch(e => {
        if (e.name !== 'AbortError') setError(true);
      });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const load = async () => {
      try {
        const response = await fetch('/api/session', {signal: controller.signal});
        if (!response.ok) throw new Error();
        const value: Session = await response.json();
        setSession(value);
        if (value.isAuthenticated) {
          const tokenResponse = await fetch('/api/auth/antiforgery', {signal: controller.signal});
          if (!tokenResponse.ok) throw new Error();
          setCsrfToken(readCsrfToken());
        }
      } catch (e) {
        if (e instanceof Error && e.name === 'AbortError') return;
        setSessionError(true);
      }
    };
    void load();
    return () => controller.abort();
  }, []);

  useEffect(() => {
    document.documentElement.lang = lang;
    document.title = brand.assistantName;
  }, [lang, brand.assistantName]);

  const t = copy[lang];
  const canChat = session?.isAuthenticated === true && session.user?.permissions?.includes('assistant.use') === true;

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const question = message.trim();
    if (!canChat || busy || !question || !csrfToken) return;

    dispatchAnswer({type: 'begin'});
    setChatError(null);
    setBusy(true);

    const controller = new AbortController();
    abortRef.current = controller;
    try {
      await runChat(csrfToken, question, conversationId, event => {
        dispatchAnswer(event);
        if (event.type === 'done') {
          setConversationId(event.conversationId);
        }
      }, controller.signal);
    } catch (e) {
      if (controller.signal.aborted) dispatchAnswer({type: 'cancelled'});
      else if (e instanceof ChatStreamError && e.code === 'timeout') dispatchAnswer({type: 'timeout'});
      else {
        dispatchAnswer({type: 'failed'});
        setChatError(t.chatError);
      }
    } finally {
      setBusy(false);
      abortRef.current = null;
    }
  }

  function stop() {
    abortRef.current?.abort();
  }
  return (
    <>
      <a className="skip" href="#main">{t.skip}</a>
      <header>
        <div className="identity">
          <img src={brand.logoUrl} alt="" width="40" height="40" />
          <div>
            <strong>{brand.assistantName}</strong>
            <small>{brand.companyName}</small>
          </div>
        </div>
        <div className="header-actions">
          <label>{t.language} <select value={lang} onChange={e => setLang(e.target.value as Lang)}><option value="es">Español</option><option value="en">English</option></select></label>
          {session?.isAuthenticated && session.user
            ? <form method="post" action="/api/auth/logout" className="session"><span>{session.user.displayName}</span><input type="hidden" name="__RequestVerificationToken" value={csrfToken} /><button type="submit" className="session-button" disabled={!csrfToken}>{t.logout}</button></form>
            : session
              ? <a className="login" href="/api/auth/login?returnUrl=/">{t.login}</a>
              : null}
        </div>
      </header>
      <main id="main">
        <span className="badge">{t.badge}</span>
        <h1>{t.title}</h1>
        <p className="intro">{t.intro}</p>
        <aside>{t.notice}</aside>
        {error && <p role="status">{t.error}</p>}
        {sessionError && <p role="alert">{t.sessionError}</p>}
        <ul className="topics">{t.topics.map(x => <li key={x}>{x}</li>)}</ul>

        <section className="composer" aria-label={t.label}>
          <form onSubmit={submit}>
            <label htmlFor="question">{t.label}</label>
            <textarea id="question" value={message} onChange={e => setMessage(e.target.value)} placeholder={t.placeholder} disabled={!canChat || busy} aria-describedby="availability" />
            <div className="composer-actions">
              {busy
                ? <button type="button" onClick={stop}>{t.stop}</button>
                : <button type="submit" disabled={!canChat || !csrfToken || !message.trim()}>{t.button}</button>}
            </div>
            <p id="availability">{canChat ? '' : (session?.isAuthenticated ? t.noPermission : t.needLogin)}</p>
          </form>
        </section>

        {phase !== 'idle' && (
          <section className="answer" aria-label={t.answerLabel}>
            <p className="answer-status" role="status">
              {phase === 'generating' ? t.provisional : phase === 'confirmed' ? t.confirmed
                : phase === 'cancelled' ? t.cancelled : phase === 'timeout' ? t.timeout : ''}
            </p>
            {sources.length > 0 && (
              <div>
                <h2 className="sources-title">{t.sourcesLabel}</h2>
                <ul className="sources">
                  {sources.map(source => <li key={source.chunkId}><span className="citation-index">[{source.index}]</span> {source.title} · v{source.version}</li>)}
                </ul>
              </div>
            )}
            <div aria-live="polite" aria-atomic="false">
              {answer && <p className="answer-text">{answer}</p>}
              {busy && !answer && <p className="pending">{t.thinking}</p>}
            </div>
            {limited && <p className="limited" role="status">{t.noAnswer}</p>}
            {chatError && <p className="chat-error" role="alert">{chatError}</p>}
          </section>
        )}
      </main>
    </>
  );
}

createRoot(document.getElementById('root')!).render(<App />);
