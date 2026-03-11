const API = {
  async getSpecs() {
    const res = await fetch('/api/specs');
    if (!res.ok) throw new Error(await res.text());
    return res.json();
  },

  async getSpec(specId) {
    const res = await fetch(`/api/specs/${encodeURIComponent(specId)}`);
    if (!res.ok) throw new Error(await res.text());
    return res.json();
  },

  async updateStatus(specId, status) {
    const res = await fetch(`/api/specs/${encodeURIComponent(specId)}/status`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ status }),
    });
    if (!res.ok) throw new Error(await res.text());
    return res.json();
  },

  async answerQuestion(specId, questionId, answer) {
    const res = await fetch(`/api/specs/${encodeURIComponent(specId)}/questions/${encodeURIComponent(questionId)}/answer`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ answer }),
    });
    if (!res.ok) throw new Error(await res.text());
    return res.json();
  },

  async submitTestResult(specId, testId, result, comment) {
    const res = await fetch(`/api/specs/${encodeURIComponent(specId)}/tests/${encodeURIComponent(testId)}/result`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ result, comment }),
    });
    if (!res.ok) throw new Error(await res.text());
    return res.json();
  },

  async getRunnerStatus() {
    const res = await fetch('/api/runner/status');
    if (!res.ok) throw new Error(await res.text());
    return res.json();
  },

  async runnerStart() {
    const res = await fetch('/api/runner/start', { method: 'POST' });
    if (!res.ok) throw new Error(await res.text());
    return res.json();
  },

  async runnerStop() {
    const res = await fetch('/api/runner/stop', { method: 'POST' });
    if (!res.ok) throw new Error(await res.text());
    return res.json();
  },

  async runnerScheduleStop() {
    const res = await fetch('/api/runner/schedule-stop', { method: 'POST' });
    if (!res.ok) throw new Error(await res.text());
    return res.json();
  },

  connectSSE(onChange, onRunner) {
    const es = new EventSource('/api/events');
    es.addEventListener('change', (e) => {
      if (onChange) onChange(JSON.parse(e.data));
    });
    es.addEventListener('runner', (e) => {
      if (onRunner) onRunner(JSON.parse(e.data));
    });
    es.addEventListener('runner-log', (e) => {
      // Could show in a log panel later
    });
    es.onerror = () => {
      // Reconnect handled automatically by EventSource
    };
    return es;
  },
};
