import {useEffect, useState} from 'react';
import {createRoot} from 'react-dom/client';
import './style.css';
type Lang = 'es'|'en';
type Branding = {companyName:string;assistantName:string;logoUrl:string;defaultLanguage:Lang};
type SessionUser = {displayName:string;roles:string[]};
type Session = {isAuthenticated:boolean;user?:SessionUser};
const copy = {
es:{language:'Idioma',badge:'Demostración · datos ficticios',title:'Tus políticas, más cerca.',intro:'Consulta vacaciones, beneficios y trabajo remoto en un solo lugar.',notice:'El acceso corporativo está disponible para usuarios asignados. El chat y las integraciones todavía no están habilitados.',topics:['Vacaciones','Beneficios','Teletrabajo'],label:'Tu pregunta',placeholder:'¿Cómo solicito vacaciones?',button:'Enviar',disabled:'El envío se habilitará cuando se integre el servicio de conversación.',error:'No se pudo cargar la configuración. Se muestra la marca de ejemplo.',skip:'Ir al contenido',login:'Iniciar sesión',logout:'Cerrar sesión',sessionError:'No se pudo comprobar la sesión o preparar el cierre seguro.'},
en:{language:'Language',badge:'Demo · fictional data',title:'Your policies, within reach.',intro:'Find leave, benefits and remote-work policies in one place.',notice:'Corporate sign-in is available for assigned users. Chat and integrations are not enabled yet.',topics:['Annual leave','Benefits','Remote work'],label:'Your question',placeholder:'How do I request leave?',button:'Send',disabled:'Sending will be enabled when the conversation service is integrated.',error:'Configuration could not be loaded. Showing sample branding.',skip:'Skip to content',login:'Sign in',logout:'Sign out',sessionError:'The session could not be checked or secure sign-out could not be prepared.'}
};
function readCsrfToken(){
const prefix='__Host-CompanyAssistant.XSRF=';
const cookie=document.cookie.split('; ').find(value=>value.startsWith(prefix));
return cookie?decodeURIComponent(cookie.slice(prefix.length)):'';
}
function App(){
const [lang,setLang]=useState<Lang>('es');
const [brand,setBrand]=useState<Branding>({companyName:'NovaWorks',assistantName:'Nova Assistant',logoUrl:'/logo.svg',defaultLanguage:'es'});
const [error,setError]=useState(false);
const [session,setSession]=useState<Session|null>(null);
const [sessionError,setSessionError]=useState(false);
const [csrfToken,setCsrfToken]=useState('');
useEffect(()=>{const controller=new AbortController();fetch('/api/branding',{signal:controller.signal}).then(r=>{if(!r.ok)throw Error();return r.json();}).then((b:Branding)=>{setBrand(b);setLang(b.defaultLanguage==='en'?'en':'es');}).catch(e=>{if(e.name!=='AbortError')setError(true);});return()=>controller.abort();},[]);
useEffect(()=>{const controller=new AbortController();const load=async()=>{try{const response=await fetch('/api/session',{signal:controller.signal});if(!response.ok)throw Error();const value:Session=await response.json();setSession(value);if(value.isAuthenticated){const tokenResponse=await fetch('/api/auth/antiforgery',{signal:controller.signal});if(!tokenResponse.ok)throw Error();setCsrfToken(readCsrfToken());}}catch(e){if(e instanceof Error&&e.name==='AbortError')return;setSessionError(true);}};void load();return()=>controller.abort();},[]);
useEffect(()=>{document.documentElement.lang=lang;document.title=brand.assistantName;},[lang,brand.assistantName]);
const t=copy[lang];
return <><a className="skip" href="#main">{t.skip}</a><header><div className="identity"><img src={brand.logoUrl} alt="" width="40" height="40"/><div><strong>{brand.assistantName}</strong><small>{brand.companyName}</small></div></div><div className="header-actions"><label>{t.language} <select value={lang} onChange={e=>setLang(e.target.value as Lang)}><option value="es">Español</option><option value="en">English</option></select></label>{session?.isAuthenticated&&session.user?<form method="post" action="/api/auth/logout" className="session"><span>{session.user.displayName}</span><input type="hidden" name="__RequestVerificationToken" value={csrfToken}/><button type="submit" className="session-button" disabled={!csrfToken}>{t.logout}</button></form>:session?<a className="login" href="/api/auth/login?returnUrl=/">{t.login}</a>:null}</div></header><main id="main"><span className="badge">{t.badge}</span><h1>{t.title}</h1><p className="intro">{t.intro}</p><aside>{t.notice}</aside>{error&&<p role="status">{t.error}</p>}{sessionError&&<p role="alert">{t.sessionError}</p>}<ul className="topics">{t.topics.map(x=><li key={x}>{x}</li>)}</ul><section className="composer" aria-label={t.label}><label htmlFor="question">{t.label}</label><textarea id="question" placeholder={t.placeholder} disabled aria-describedby="availability"/><button disabled>{t.button}</button><p id="availability">{t.disabled}</p></section></main></>;
}
createRoot(document.getElementById('root')!).render(<App/>);
