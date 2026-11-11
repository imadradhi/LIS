/**
 * MedLab LIS - Medical Report & Document PDF Exporter
 * Generates crisp client-side A4 PDFs from DOM elements using html2pdf.js
 */
window.lisExportReportToPdf = function (elementId, fileName) {
    return new Promise(function (resolve, reject) {
        try {
            var el = document.getElementById(elementId);
            if (!el) {
                console.error("[lisExportReportToPdf] Element not found:", elementId);
                reject("Element not found: " + elementId);
                return;
            }

            // Find medical report container or use element directly
            var target = el.querySelector('.medical-report-container') || el;

            // Generate clean default filename if not passed
            if (!fileName || fileName.trim() === '') {
                fileName = 'Medical_Report_' + new Date().toISOString().slice(0, 10) + '.pdf';
            }
            if (!fileName.toLowerCase().endsWith('.pdf')) {
                fileName += '.pdf';
            }

            // If html2pdf is available
            if (typeof html2pdf !== 'undefined') {
                var opt = {
                    margin: [8, 8, 8, 8], // mm
                    filename: fileName,
                    image: { type: 'jpeg', quality: 0.98 },
                    html2canvas: {
                        scale: 2,
                        useCORS: true,
                        logging: false,
                        backgroundColor: '#ffffff',
                        letterRendering: true,
                        scrollY: 0,
                        scrollX: 0
                    },
                    jsPDF: {
                        unit: 'mm',
                        format: 'a4',
                        orientation: 'portrait'
                    },
                    pagebreak: { mode: ['avoid-all', 'css', 'legacy'] }
                };

                html2pdf().set(opt).from(target).save().then(function () {
                    resolve(true);
                }).catch(function (err) {
                    console.error("[lisExportReportToPdf] html2pdf save failed:", err);
                    // Fallback to print
                    window.print();
                    resolve(false);
                });
            } else {
                console.warn("[lisExportReportToPdf] html2pdf not available, falling back to print dialog.");
                window.print();
                resolve(true);
            }
        } catch (err) {
            console.error("[lisExportReportToPdf] Exception:", err);
            reject(err);
        }
    });
};

window.lisExportElementToPdf = function (elementId, fileName, format) {
    return new Promise(function (resolve, reject) {
        try {
            var el = document.getElementById(elementId);
            if (!el) {
                console.error("[lisExportElementToPdf] Element not found:", elementId);
                reject("Element not found: " + elementId);
                return;
            }

            if (!fileName || fileName.trim() === '') {
                fileName = 'Document_' + new Date().toISOString().slice(0, 10) + '.pdf';
            }
            if (!fileName.toLowerCase().endsWith('.pdf')) {
                fileName += '.pdf';
            }

            if (typeof html2pdf !== 'undefined') {
                var opt = {
                    margin: [6, 6, 6, 6],
                    filename: fileName,
                    image: { type: 'jpeg', quality: 0.98 },
                    html2canvas: {
                        scale: 2,
                        useCORS: true,
                        logging: false,
                        backgroundColor: '#ffffff'
                    },
                    jsPDF: {
                        unit: 'mm',
                        format: format || 'a4',
                        orientation: 'portrait'
                    }
                };

                html2pdf().set(opt).from(el).save().then(function () {
                    resolve(true);
                }).catch(function (err) {
                    console.error("[lisExportElementToPdf] html2pdf failed:", err);
                    window.print();
                    resolve(false);
                });
            } else {
                window.print();
                resolve(true);
            }
        } catch (err) {
            console.error("[lisExportElementToPdf] Exception:", err);
            reject(err);
        }
    });
};
