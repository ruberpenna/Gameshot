// 찜 하트: 폼 제출 대신 fetch로 토글해서 페이지가 이동하지 않게 한다 (스크립트가 없으면 폼 제출로 동작)
document.addEventListener('submit', async (e) => {
    const form = e.target;
    if (!(form instanceof HTMLFormElement) || !form.classList.contains('wish-form')) return;
    e.preventDefault();

    const button = form.querySelector('button');
    button.disabled = true;
    try {
        const res = await fetch(form.action, {
            method: 'POST',
            body: new FormData(form),
            headers: { 'Accept': 'application/json' },
        });
        if (!res.ok) throw new Error(res.status);
        const { wished } = await res.json();

        // 같은 상품의 하트가 한 화면에 여러 개 있을 수 있어 모두 맞춘다
        document.querySelectorAll(`form.wish-form[action="${form.getAttribute('action')}"] button`).forEach(b => {
            b.classList.toggle('is-wished', wished);
            b.setAttribute('aria-pressed', String(wished));
            b.setAttribute('aria-label', wished ? '찜 해제' : '찜하기');
            b.title = wished ? '찜 해제' : '찜하기';
            b.querySelector('.wish-icon').textContent = wished ? '♥' : '♡';
        });
    } catch {
        form.submit();   // 실패하면 일반 제출로 (로그인 만료 등은 서버가 알아서 안내)
    } finally {
        button.disabled = false;
    }
});
