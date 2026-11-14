import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import { zhCN, en } from './locales';

const stored = localStorage.getItem('sh.lang');
const browser = navigator.languages.find((language) => language.toLowerCase().startsWith('zh')) ? 'zh-CN' : 'en';

void i18n.use(initReactI18next).init({
  resources: {
    'zh-CN': { translation: zhCN },
    en: { translation: en }
  },
  lng: stored ?? browser,
  fallbackLng: 'zh-CN',
  interpolation: { escapeValue: false }
});

export function setLanguage(lang: string) {
  localStorage.setItem('sh.lang', lang);
  document.documentElement.lang = lang;
  void i18n.changeLanguage(lang);
}

document.documentElement.lang = i18n.language;

export default i18n;
