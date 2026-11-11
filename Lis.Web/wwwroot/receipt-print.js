/**
 * LIS Web - Cashier Thermal Receipt Isolated 80mm Printing Helper
 */
window.lisPrintThermalReceipt = function (elementId) {
    const el = document.getElementById(elementId);
    if (!el) {
        window.print();
        return;
    }

    let iframe = document.getElementById('thermal-print-frame');
    if (!iframe) {
        iframe = document.createElement('iframe');
        iframe.id = 'thermal-print-frame';
        iframe.style.position = 'fixed';
        iframe.style.right = '0';
        iframe.style.bottom = '0';
        iframe.style.width = '0';
        iframe.style.height = '0';
        iframe.style.border = '0';
        iframe.style.zIndex = '-9999';
        document.body.appendChild(iframe);
    }

    const doc = iframe.contentWindow.document;
    doc.open();
    doc.write(`
        <!DOCTYPE html>
        <html lang="ar" dir="rtl">
        <head>
            <meta charset="utf-8">
            <title>وصل كاشير - طباعة</title>
            <style>
                @page {
                    size: 80mm auto;
                    margin: 0;
                }
                * {
                    box-sizing: border-box;
                    margin: 0;
                    padding: 0;
                }
                body {
                    font-family: 'Segoe UI', Tahoma, Arial, sans-serif;
                    background: #fff;
                    color: #000;
                    width: 72mm;
                    margin: 0 auto;
                    padding: 4mm 2mm;
                    direction: rtl;
                    line-height: 1.35;
                }
                .thermal-content {
                    width: 100%;
                }
                .text-center { text-align: center; }
                .text-start { text-align: right; }
                .text-end { text-align: left; }
                .fw-bold { font-weight: bold; }
                .receipt-header-title {
                    font-size: 17px;
                    font-weight: 800;
                    margin-bottom: 2px;
                }
                .receipt-header-sub {
                    font-size: 13.5px;
                    font-weight: 600;
                    margin-bottom: 3px;
                }
                .receipt-header-contact {
                    font-size: 11px;
                    margin-bottom: 4px;
                }
                .receipt-solid-divider {
                    border-bottom: 2px solid #000;
                    margin: 7px 0;
                }
                .receipt-dashed-divider {
                    border-bottom: 1.5px dashed #000;
                    margin: 7px 0;
                }
                .receipt-meta-grid {
                    display: flex;
                    justify-content: space-between;
                    font-size: 11.5px;
                    line-height: 1.5;
                }
                .receipt-meta-right {
                    text-align: right;
                    width: 55%;
                }
                .receipt-meta-left {
                    text-align: left;
                    width: 45%;
                }
                .receipt-table {
                    width: 100%;
                    border-collapse: collapse;
                    font-size: 12px;
                    margin: 4px 0;
                }
                .receipt-table th {
                    padding: 4px 0;
                    font-weight: bold;
                    border-bottom: 1.5px solid #000;
                }
                .receipt-table td {
                    padding: 4px 0;
                    vertical-align: top;
                }
                .th-test, .td-test {
                    text-align: right;
                }
                .th-price, .td-price {
                    text-align: left;
                    font-weight: bold;
                    font-family: monospace;
                    font-size: 12.5px;
                }
                .receipt-summary-row {
                    display: flex;
                    justify-content: space-between;
                    font-size: 11.5px;
                    padding: 2px 0;
                }
                .paid-box {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    font-size: 16px;
                    font-weight: 800;
                    padding: 3px 0;
                }
                .paid-box .paid-amount {
                    font-size: 17px;
                    font-family: 'Segoe UI', Tahoma, monospace, sans-serif;
                }
                .footer-box {
                    text-align: center;
                    padding-top: 6px;
                }
                .footer-thanks-ar {
                    font-size: 15px;
                    font-weight: 800;
                    margin-bottom: 2px;
                }
                .footer-thanks-en {
                    font-size: 13px;
                    font-weight: 600;
                }
            </style>
        </head>
        <body>
            <div class="thermal-content">
                ${el.innerHTML}
            </div>
        </body>
        </html>
    `);
    doc.close();

    setTimeout(() => {
        iframe.contentWindow.focus();
        iframe.contentWindow.print();
    }, 200);
};

