import { ROUTES } from '../hooks/useHashRoute.js'

export default function Nav({ route }) {
  return (
    <nav className="nav" aria-label="Sections">
      {Object.entries(ROUTES).map(([key, value]) => (
        <a
          key={key}
          className={`nav__link ${route === key ? 'nav__link--active' : ''}`}
          href={value.path}
          aria-current={route === key ? 'page' : undefined}
        >
          {value.label}
        </a>
      ))}
    </nav>
  )
}
