function scoreTone(score) {
  if (score >= 70) return 'high'
  if (score >= 45) return 'medium'
  return 'low'
}

const toneLabels = {
  high: 'Strong opportunity',
  medium: 'Worth watching',
  low: 'No action',
}

export default function ScorePanel({ breakdowns }) {
  return (
    <section className="panel" aria-label="Opportunity scores">
      <div className="panel__head">
        <h2 className="panel__title">Opportunity scores</h2>
        <span className="panel__hint">0–100 · decision support only</span>
      </div>

      <div className="scores">
        {breakdowns.map((entry) => {
          const tone = scoreTone(entry.score)

          return (
            <article key={entry.symbol} className="score">
              <div className="score__head">
                <div
                  className={`score__gauge score__gauge--${tone}`}
                  style={{ '--score': entry.score }}
                >
                  <span className="score__gauge-value">{entry.score}</span>
                </div>
                <div>
                  <p className="symbol">{entry.symbol}</p>
                  <p className={`score__tone score__tone--${tone}`}>{toneLabels[tone]}</p>
                </div>
              </div>

              <ul className="score__factors">
                {entry.factors.map((factor) => (
                  <li key={factor.key} className="score__factor">
                    <div className="score__factor-head">
                      <span>{factor.label}</span>
                      <span className="num">{Math.round(factor.weight)}</span>
                    </div>
                    <div className="score__factor-bar">
                      <span
                        className={`score__factor-fill score__factor-fill--${tone}`}
                        style={{ width: `${factor.weight}%` }}
                      />
                    </div>
                    <p className="subtle">{factor.detail}</p>
                  </li>
                ))}
              </ul>
            </article>
          )
        })}
      </div>
    </section>
  )
}
