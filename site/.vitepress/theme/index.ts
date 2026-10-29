import DefaultTheme from 'vitepress/theme'
import PrismHome from '../../components/PrismHome.vue'
import RibbonCatalog from '../../components/RibbonCatalog.vue'
import './style.css'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    app.component('PrismHome', PrismHome)
    app.component('RibbonCatalog', RibbonCatalog)
  },
}
