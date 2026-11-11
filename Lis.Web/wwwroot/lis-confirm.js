/**
 * MedLab LIS - Safe Operation Confirmation Dialog (MedLab Clinical Design)
 * Provides styled confirmation modal for sensitive operations (Delete, Cancel, Clear, Exit).
 */
window.lisConfirm = function (title, message, confirmText, cancelText, type) {
    return new Promise(function (resolve) {
        try {
            var existing = document.getElementById('lis-confirm-modal');
            if (existing) existing.remove();

            var isDark = document.documentElement.getAttribute('data-mdb-theme') === 'dark' || 
                         document.documentElement.classList.contains('lis-dark-theme');
            var isAr = document.documentElement.getAttribute('data-lis-lang') === 'ar' || 
                       document.documentElement.getAttribute('lang') === 'ar';

            title = title || (isAr ? 'تأكيد العملية للأمان' : 'Security Confirmation');
            message = message || (isAr ? 'هل أنت متأكد من رغبتك في إتمام هذه العملية؟' : 'Are you sure you want to proceed?');
            confirmText = confirmText || (isAr ? 'تأكيد الإجراء' : 'Confirm');
            cancelText = cancelText || (isAr ? 'تراجع وإلغاء' : 'Cancel');
            type = type || 'danger'; // 'danger' | 'warning' | 'info'

            var iconClass = type === 'warning' ? 'fa-triangle-exclamation text-warning' : 
                            type === 'danger' ? 'fa-trash-can text-danger' : 
                            'fa-shield-halved text-primary';

            var btnClass = type === 'warning' ? 'btn-warning text-dark' : 
                           type === 'danger' ? 'btn-danger' : 
                           'btn-primary';

            var bgCard = isDark ? '#1e293b' : '#ffffff';
            var textColor = isDark ? '#f8fafc' : '#0f172a';
            var textMuted = isDark ? '#94a3b8' : '#64748b';
            var borderColor = isDark ? '#334155' : '#cbd5e1';

            var modalHtml = `
            <div id="lis-confirm-modal" class="modal fade show d-block" tabindex="-1" style="background: rgba(0,0,0,0.8); backdrop-filter: blur(4px); z-index: 999999; direction: ${isAr ? 'rtl' : 'ltr'};">
                <div class="modal-dialog modal-dialog-centered" style="max-width: 440px;">
                    <div class="modal-content shadow-lg border-0" style="background: ${bgCard}; color: ${textColor}; border-radius: 12px; border: 1px solid ${borderColor} !important;">
                        <div class="modal-body p-4 text-center">
                            <div class="mb-3 d-inline-flex align-items-center justify-content-center rounded-circle" style="width: 56px; height: 56px; background: ${type === 'danger' ? 'rgba(239, 68, 68, 0.15)' : 'rgba(245, 158, 11, 0.15)'}; font-size: 24px;">
                                <i class="fas ${iconClass}"></i>
                            </div>
                            <h5 class="fw-bold mb-2" style="color: ${textColor}; font-size: 16px;">${title}</h5>
                            <p class="mb-4 small px-2" style="color: ${textMuted}; line-height: 1.6; font-size: 13px;">${message}</p>
                            <div class="d-flex justify-content-center gap-2">
                                <button type="button" id="lis-confirm-btn-cancel" class="btn btn-outline-secondary btn-sm px-4 fw-bold" style="border-radius: 8px;">
                                    ${cancelText}
                                </button>
                                <button type="button" id="lis-confirm-btn-ok" class="btn ${btnClass} btn-sm px-4 fw-bold shadow-sm" style="border-radius: 8px;">
                                    ${confirmText}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>`;

            var div = document.createElement('div');
            div.innerHTML = modalHtml;
            var modalEl = div.firstElementChild;
            document.body.appendChild(modalEl);

            var cleanup = function (res) {
                if (modalEl && modalEl.parentNode) {
                    modalEl.parentNode.removeChild(modalEl);
                }
                resolve(res);
            };

            var btnOk = document.getElementById('lis-confirm-btn-ok');
            var btnCancel = document.getElementById('lis-confirm-btn-cancel');

            if (btnOk) {
                btnOk.onclick = function () { cleanup(true); };
                btnOk.focus();
            }
            if (btnCancel) {
                btnCancel.onclick = function () { cleanup(false); };
            }

            modalEl.onclick = function (e) {
                if (e.target === modalEl) cleanup(false);
            };

            // Support Escape key to cancel
            var onKeyDown = function (e) {
                if (e.key === 'Escape') {
                    document.removeEventListener('keydown', onKeyDown);
                    cleanup(false);
                }
            };
            document.addEventListener('keydown', onKeyDown);
        } catch (e) {
            console.warn('[lisConfirm] Fallback to native confirm:', e);
            resolve(window.confirm(message || title));
        }
    });
};

