import { h } from 'vue'
import DefaultTheme from 'vitepress/theme'
import AlphaBanner from '../../components/AlphaBanner.vue'
import PrismHome from '../../components/PrismHome.vue'
import RibbonCatalog from '../../components/RibbonCatalog.vue'
import './style.css'

export default {
  extends: DefaultTheme,
  // Prism is not stable yet; every page says so above the navigation.
  Layout: () => h(DefaultTheme.Layout, null, { 'layout-top': () => h(AlphaBanner) }),
  enhanceApp({ app }) {
    app.component('PrismHome', PrismHome)
    app.component('RibbonCatalog', RibbonCatalog)
  },
}
