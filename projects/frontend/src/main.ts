import { createApp as createVueApp } from 'vue'
import { createPinia } from 'pinia'

import App from './App.vue'
import router from './router'
import i18n from './i18n'
import './assets/styles/main.css'

// Font Awesome
import { library } from '@fortawesome/fontawesome-svg-core'
import { faHome, faTachometerAlt, faTrophy, faBook, faSignInAlt, faSignOutAlt, faBars, faChartBar, faLandmark } from '@fortawesome/free-solid-svg-icons'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'

library.add(faHome, faTachometerAlt, faTrophy, faBook, faSignInAlt, faSignOutAlt, faBars, faChartBar, faLandmark)

export function createApp() {
  const app = createVueApp(App)
  const pinia = createPinia()

  app.use(pinia)
  app.use(router)
  app.use(i18n)

  // Register Font Awesome component
  app.component('font-awesome-icon', FontAwesomeIcon)

  return { app, router, pinia }
}

// Client-side mounting
const { app } = createApp()
app.mount('#app')
