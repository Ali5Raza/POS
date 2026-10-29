const POS = (function () {
    let cart = [];
    let selectedProductId = null;
    let stagedProduct = null;
    let activeCategoryId = 0;
    let currentHeldSaleId = null;
    let lastReceiptUrl = null;
    let searchDebounceTimer = null;

    function getAntiForgeryToken() {
        const input = document.querySelector('#posAntiForgeryForm input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function showMessage(msg, type = 'danger') {
        const box = document.getElementById('posAlertBox');
        if (!box) return;
        box.className = `alert alert-${type} py-1 px-3 mb-2 shadow-sm small`;
        box.innerHTML = `<i class="bi ${type === 'success' ? 'bi-check-circle-fill' : 'bi-exclamation-triangle-fill'} me-2"></i><strong>${msg}</strong>`;
        box.classList.remove('d-none');
        setTimeout(() => box.classList.add('d-none'), 5000);
    }

    function getPriceForTier(salePrice, wholesalePrice) {
        const tier = document.getElementById('posPriceTier')?.value || 'Retail';
        if (tier === 'Wholesale' && wholesalePrice > 0) {
            return wholesalePrice;
        }
        return salePrice;
    }

    function updateLedDisplay(title, meta) {
        const titleEl = document.getElementById('posLedItemName');
        const metaEl = document.getElementById('posLedItemMeta');
        if (titleEl) titleEl.textContent = title;
        if (metaEl) metaEl.textContent = meta;
    }

    function updateEntryPreview() {
        const qty = parseFloat(document.getElementById('posEntryQty')?.value) || 1;
        const rate = parseFloat(document.getElementById('posEntryRate')?.value) || 0;
        const amtEl = document.getElementById('posEntryAmount');
        if (amtEl) {
            const amt = qty * rate;
            amtEl.value = amt > 0 ? amt.toFixed(2) : '.00';
        }
    }

    function getAllCatalogProductsFromDom() {
        const tiles = document.querySelectorAll('#posProductGrid .pos-dense-product-tile');
        const list = [];
        tiles.forEach(tile => {
            list.push({
                id: parseInt(tile.getAttribute('data-product-id')),
                sku: tile.getAttribute('data-sku') || '',
                barcode: tile.getAttribute('data-barcode') || '',
                name: tile.getAttribute('data-name') || '',
                salePrice: parseFloat(tile.getAttribute('data-sale-price')) || 0,
                wholesalePrice: parseFloat(tile.getAttribute('data-wholesale-price')) || 0,
                minimumSalePrice: parseFloat(tile.getAttribute('data-min-price')) || 0,
                taxRate: parseFloat(tile.getAttribute('data-tax-rate')) || 0,
                currentStock: parseFloat(tile.getAttribute('data-stock')) || 0,
                unit: tile.getAttribute('data-unit') || 'pcs'
            });
        });
        return list;
    }

    function stageProductInTopBar(p) {
        stagedProduct = p;
        const unitPrice = getPriceForTier(p.salePrice, p.wholesalePrice);
        const codeInput = document.getElementById('posBarcodeInput');
        const qtyInput = document.getElementById('posEntryQty');
        const rateInput = document.getElementById('posEntryRate');

        if (codeInput) codeInput.value = p.sku || p.barcode;
        if (qtyInput && (!qtyInput.value || parseFloat(qtyInput.value) <= 0)) qtyInput.value = '1.00';
        if (rateInput) rateInput.value = unitPrice.toFixed(2);

        updateEntryPreview();
        updateLedDisplay(
            `${p.sku} — ${p.name.toUpperCase()}`,
            `RATE: ${unitPrice.toFixed(2)} | STOCK: ${p.currentStock} ${p.unit} | BARCODE: ${p.barcode}`
        );
        hideSuggestions();
    }

    function addProductToCart(p, customQty = null, customRate = null) {
        const allowNeg = window.POS_CONFIG?.allowNegativeStock === true;
        const addQty = customQty !== null && customQty > 0 ? customQty : 1;
        const existing = cart.find(x => x.productId === p.id);
        const newQty = existing ? existing.quantity + addQty : addQty;

        if (!allowNeg && newQty > p.currentStock) {
            showMessage(`Insufficient stock for "${p.name}". Available: ${p.currentStock}`, 'warning');
            return;
        }

        const defaultPrice = getPriceForTier(p.salePrice, p.wholesalePrice);
        const unitPrice = (customRate !== null && customRate > 0 && window.POS_CONFIG?.canOverridePrice)
            ? customRate
            : defaultPrice;

        if (existing) {
            existing.quantity = newQty;
            if (customRate !== null && customRate > 0 && window.POS_CONFIG?.canOverridePrice) {
                existing.unitPrice = unitPrice;
            }
        } else {
            cart.push({
                productId: p.id,
                sku: p.sku,
                barcode: p.barcode,
                name: p.name,
                quantity: addQty,
                salePrice: p.salePrice,
                wholesalePrice: p.wholesalePrice || p.salePrice,
                minPrice: p.minimumSalePrice || 0,
                unitPrice: unitPrice,
                discountAmount: 0,
                taxRate: window.POS_CONFIG?.enableTax ? (p.taxRate || 0) : 0,
                stock: p.currentStock,
                unit: p.unit || 'pcs'
            });
        }

        selectedProductId = p.id;
        stagedProduct = null;

        // Show added item in Top Entry Bar & LED Display
        const lineAmt = newQty * unitPrice;
        const amtEl = document.getElementById('posEntryAmount');
        const rateEl = document.getElementById('posEntryRate');
        const qtyEl = document.getElementById('posEntryQty');
        const codeEl = document.getElementById('posBarcodeInput');

        if (rateEl) rateEl.value = unitPrice.toFixed(2);
        if (amtEl) amtEl.value = lineAmt.toFixed(2);
        if (qtyEl) qtyEl.value = '1.00';
        if (codeEl) {
            codeEl.value = '';
            codeEl.focus();
        }

        updateLedDisplay(
            `${p.sku} — ${p.name.toUpperCase()}`,
            `QTY: ${newQty.toFixed(2)} x ${unitPrice.toFixed(2)} = ${lineAmt.toFixed(2)} | STOCK LEFT: ${(p.currentStock - newQty)}`
        );

        hideSuggestions();
        renderCart();
    }

    function addProductFromCard(cardEl) {
        const p = {
            id: parseInt(cardEl.getAttribute('data-product-id')),
            sku: cardEl.getAttribute('data-sku'),
            barcode: cardEl.getAttribute('data-barcode'),
            name: cardEl.getAttribute('data-name'),
            salePrice: parseFloat(cardEl.getAttribute('data-sale-price')) || 0,
            wholesalePrice: parseFloat(cardEl.getAttribute('data-wholesale-price')) || 0,
            minimumSalePrice: parseFloat(cardEl.getAttribute('data-min-price')) || 0,
            taxRate: parseFloat(cardEl.getAttribute('data-tax-rate')) || 0,
            currentStock: parseFloat(cardEl.getAttribute('data-stock')) || 0,
            unit: cardEl.getAttribute('data-unit') || 'pcs'
        };
        const entryQty = parseFloat(document.getElementById('posEntryQty')?.value) || 1;
        addProductToCart(p, entryQty, null);
    }

    async function commitEntryRow() {
        const codeInput = document.getElementById('posBarcodeInput');
        const qty = parseFloat(document.getElementById('posEntryQty')?.value) || 1;
        const rate = parseFloat(document.getElementById('posEntryRate')?.value) || null;

        if (stagedProduct) {
            addProductToCart(stagedProduct, qty, rate);
            return;
        }

        const rawCode = (codeInput?.value || '').trim();
        if (!rawCode) {
            codeInput?.focus();
            return;
        }

        await handleBarcodeScan(rawCode, qty, rate);
    }

    async function handleBarcodeScan(code, customQty = null, customRate = null) {
        if (!code || !code.trim()) return;
        const cleanCode = code.trim();
        const qty = customQty !== null ? customQty : (parseFloat(document.getElementById('posEntryQty')?.value) || 1);

        // 1. Exact Barcode or SKU match in local catalog
        const allProds = getAllCatalogProductsFromDom();
        const exact = allProds.find(p =>
            p.barcode.toLowerCase() === cleanCode.toLowerCase() ||
            p.sku.toLowerCase() === cleanCode.toLowerCase()
        );
        if (exact) {
            addProductToCart(exact, qty, customRate);
            return;
        }

        // 2. Partial name/code match if only 1 or top suggestion exists
        const partial = allProds.filter(p =>
            p.name.toLowerCase().includes(cleanCode.toLowerCase()) ||
            p.sku.toLowerCase().includes(cleanCode.toLowerCase()) ||
            p.barcode.toLowerCase().includes(cleanCode.toLowerCase())
        );
        if (partial.length === 1) {
            addProductToCart(partial[0], qty, customRate);
            return;
        }

        // 3. Fallback to server barcode lookup
        try {
            const resp = await fetch(`/POS/ScanBarcode?code=${encodeURIComponent(cleanCode)}`);
            const data = await resp.json();
            if (data.found && data.product) {
                addProductToCart(data.product, qty, customRate);
            } else if (partial.length > 1) {
                addProductToCart(partial[0], qty, customRate);
            } else {
                showMessage(data.message || `No product found for code "${cleanCode}"`, 'warning');
            }
        } catch {
            showMessage('Error looking up item code.', 'danger');
        }
    }

    function showSuggestions(term) {
        const menu = document.getElementById('posCodeSuggestions');
        if (!menu) return;

        const clean = (term || '').trim().toLowerCase();
        if (!clean) {
            hideSuggestions();
            return;
        }

        const matches = getAllCatalogProductsFromDom().filter(p =>
            p.sku.toLowerCase().includes(clean) ||
            p.barcode.toLowerCase().includes(clean) ||
            p.name.toLowerCase().includes(clean)
        ).slice(0, 8);

        if (matches.length === 0) {
            hideSuggestions();
            return;
        }

        menu.innerHTML = matches.map(p => {
            const price = getPriceForTier(p.salePrice, p.wholesalePrice);
            return `
                <div class="pos-autocomplete-item" onclick="POS.selectSuggestion(${p.id})">
                    <div>
                        <strong>${p.sku}</strong> &mdash; ${p.name}
                        <small class="opacity-75 ms-1">(${p.barcode})</small>
                    </div>
                    <div>
                        <span class="badge bg-secondary me-1">Stk: ${p.currentStock}</span>
                        <strong>${price.toFixed(2)}</strong>
                    </div>
                </div>`;
        }).join('');
        menu.classList.remove('d-none');
    }

    function hideSuggestions() {
        const menu = document.getElementById('posCodeSuggestions');
        if (menu) menu.classList.add('d-none');
    }

    function selectSuggestion(productId) {
        const prod = getAllCatalogProductsFromDom().find(x => x.id === productId);
        if (!prod) return;
        const qty = parseFloat(document.getElementById('posEntryQty')?.value) || 1;
        addProductToCart(prod, qty, null);
    }

    function selectBasketRow(productId) {
        selectedProductId = productId;
        const item = cart.find(x => x.productId === productId);
        if (item) {
            const lineAmt = item.quantity * item.unitPrice;
            updateLedDisplay(
                `${item.sku} — ${item.name.toUpperCase()}`,
                `SELECTED IN BASKET | QTY: ${item.quantity.toFixed(2)} | RATE: ${item.unitPrice.toFixed(2)} | AMT: ${lineAmt.toFixed(2)}`
            );
        }
        document.querySelectorAll('#posCartBody tr').forEach(tr => {
            const rId = parseInt(tr.getAttribute('data-row-product-id'));
            tr.classList.toggle('pos-row-selected', rId === selectedProductId);
        });
    }

    function changeQty(productId, delta) {
        const item = cart.find(x => x.productId === productId);
        if (!item) return;

        const nextQty = item.quantity + delta;
        if (nextQty <= 0) {
            removeItem(productId);
            return;
        }

        const allowNeg = window.POS_CONFIG?.allowNegativeStock === true;
        if (!allowNeg && nextQty > item.stock) {
            showMessage(`Cannot exceed available stock (${item.stock}) for "${item.name}".`, 'warning');
            return;
        }

        item.quantity = nextQty;
        selectedProductId = productId;
        renderCart();
    }

    function setQty(productId, val) {
        const item = cart.find(x => x.productId === productId);
        if (!item) return;

        const qty = parseFloat(val);
        if (isNaN(qty) || qty <= 0) {
            removeItem(productId);
            return;
        }

        const allowNeg = window.POS_CONFIG?.allowNegativeStock === true;
        if (!allowNeg && qty > item.stock) {
            showMessage(`Available stock for "${item.name}" is ${item.stock}.`, 'warning');
            item.quantity = item.stock > 0 ? item.stock : 1;
        } else {
            item.quantity = qty;
        }
        selectedProductId = productId;
        renderCart();
    }

    function setUnitPrice(productId, val) {
        const item = cart.find(x => x.productId === productId);
        if (!item) return;

        if (!window.POS_CONFIG?.canOverridePrice) {
            showMessage('You do not have permission to override product unit prices.', 'warning');
            renderCart();
            return;
        }

        const price = parseFloat(val);
        if (isNaN(price) || price <= 0) {
            renderCart();
            return;
        }

        item.unitPrice = price;
        selectedProductId = productId;
        renderCart();
    }

    function setItemDiscount(productId, val) {
        const item = cart.find(x => x.productId === productId);
        if (!item) return;

        const disc = Math.max(0, parseFloat(val) || 0);
        const lineGross = item.quantity * item.unitPrice;
        const maxPct = window.POS_CONFIG?.maxNormalDiscountPercent || 20;

        if (lineGross > 0 && ((disc / lineGross) * 100) > maxPct && !window.POS_CONFIG?.canApplyHighDiscount) {
            showMessage(`Discount exceeds maximum allowed ${maxPct}%. Supervisor permission required.`, 'warning');
            item.discountAmount = Math.floor((lineGross * maxPct) / 100);
        } else {
            item.discountAmount = Math.min(lineGross, disc);
        }

        selectedProductId = productId;
        renderCart();
    }

    function removeItem(productId) {
        cart = cart.filter(x => x.productId !== productId);
        if (selectedProductId === productId) {
            selectedProductId = cart.length > 0 ? cart[cart.length - 1].productId : null;
        }
        renderCart();
    }

    function clearCart() {
        cart = [];
        selectedProductId = null;
        stagedProduct = null;
        currentHeldSaleId = null;
        document.getElementById('posInvoiceDiscount').value = '0.00';
        document.getElementById('posPaidAmount').value = '0.00';
        document.getElementById('posEntryQty').value = '1.00';
        document.getElementById('posEntryRate').value = '';
        document.getElementById('posEntryAmount').value = '.00';
        updateLedDisplay('READY TO SCAN — ENTER ITEM CODE OR SELECT PRODUCT BELOW', 'SKU: --- | STOCK: --- | BARCODE READER ACTIVE');
        renderCart();
        document.getElementById('posBarcodeInput')?.focus();
    }

    function onPriceTierChange() {
        const tier = document.getElementById('posPriceTier')?.value || 'Retail';
        document.querySelectorAll('.pos-card-price').forEach(el => {
            const r = parseFloat(el.getAttribute('data-retail')) || 0;
            const w = parseFloat(el.getAttribute('data-wholesale')) || r;
            el.textContent = (tier === 'Wholesale' ? w : r).toFixed(0);
        });

        cart.forEach(item => {
            item.unitPrice = (tier === 'Wholesale' && item.wholesalePrice > 0) ? item.wholesalePrice : item.salePrice;
        });
        renderCart();
    }

    function renderCart() {
        const tbody = document.getElementById('posCartBody');
        if (!tbody) return;

        if (cart.length === 0) {
            tbody.innerHTML = `
                <tr id="posEmptyCartRow">
                    <td colspan="8" class="text-center text-muted py-4" style="background:transparent;">
                        Scan barcode / type Item Code above or click any product below to add to basket
                    </td>
                </tr>`;
            recalculate(true);
            return;
        }

        if (!selectedProductId) {
            selectedProductId = cart[cart.length - 1].productId;
        }

        const canOverride = window.POS_CONFIG?.canOverridePrice === true;
        let rowsHtml = '';

        cart.forEach((item, idx) => {
            const lineGross = item.quantity * item.unitPrice;
            const netLine = Math.max(0, lineGross - item.discountAmount);
            const taxAmt = netLine * (item.taxRate / 100);
            const lineTotal = netLine + taxAmt;
            const isSelected = item.productId === selectedProductId;

            rowsHtml += `
                <tr data-row-product-id="${item.productId}" class="${isSelected ? 'pos-row-selected' : ''}" onclick="POS.selectBasketRow(${item.productId})">
                    <td class="text-center">${idx + 1}</td>
                    <td>${item.sku}</td>
                    <td class="text-truncate" style="max-width: 260px;">${item.name}</td>
                    <td class="text-end">
                        <input type="number" class="pos-inline-cell-input" value="${item.quantity.toFixed(2)}" min="0.01" step="1"
                               onclick="event.stopPropagation()"
                               onchange="POS.setQty(${item.productId}, this.value)" />
                    </td>
                    <td class="text-end">
                        <input type="number" class="pos-inline-cell-input" value="${item.unitPrice.toFixed(2)}" step="0.01"
                               ${canOverride ? '' : 'readonly'}
                               onclick="event.stopPropagation()"
                               onchange="POS.setUnitPrice(${item.productId}, this.value)" />
                    </td>
                    <td class="text-end">
                        <input type="number" class="pos-inline-cell-input" value="${item.discountAmount.toFixed(2)}" min="0" step="1"
                               onclick="event.stopPropagation()"
                               onchange="POS.setItemDiscount(${item.productId}, this.value)" />
                    </td>
                    <td class="text-end fw-bold">${lineTotal.toFixed(2)}</td>
                    <td class="text-center">
                        <button type="button" class="btn btn-link btn-sm p-0 text-danger" style="line-height:1;"
                                onclick="event.stopPropagation(); POS.removeItem(${item.productId})" title="Remove">
                            &times;
                        </button>
                    </td>
                </tr>`;
        });

        tbody.innerHTML = rowsHtml;
        recalculate(true);
    }

    function recalculate(autoSyncPaid = false) {
        let subTotal = 0;
        let itemDiscounts = 0;
        let totalQty = 0;

        for (const item of cart) {
            const gross = item.quantity * item.unitPrice;
            subTotal += gross;
            itemDiscounts += item.discountAmount;
            totalQty += item.quantity;
        }

        const invDiscInput = document.getElementById('posInvoiceDiscount');
        let invDisc = Math.max(0, parseFloat(invDiscInput?.value) || 0);
        const totalDiscount = itemDiscounts + invDisc;
        const grandTotal = Math.max(0, subTotal - totalDiscount);

        const linesEl = document.getElementById('posTotalLinesCount');
        if (linesEl) linesEl.textContent = cart.length;
        document.getElementById('posTotalItemsCount').textContent = totalQty;
        document.getElementById('posSubTotalDisplay').textContent = subTotal.toFixed(2);
        document.getElementById('posGrandTotalDisplay').textContent = grandTotal.toFixed(2);

        const payMethod = document.getElementById('posPaymentMethod')?.value || 'Cash';
        const paidInput = document.getElementById('posPaidAmount');

        if (autoSyncPaid && paidInput) {
            if (payMethod === 'Credit') {
                paidInput.value = '0.00';
            } else {
                paidInput.value = grandTotal.toFixed(2);
            }
        }

        updateChangeAndDue();
    }

    function updateChangeAndDue() {
        const grandTotal = parseFloat(document.getElementById('posGrandTotalDisplay')?.textContent) || 0;
        const paid = Math.max(0, parseFloat(document.getElementById('posPaidAmount')?.value) || 0);
        const method = document.getElementById('posPaymentMethod')?.value || 'Cash';

        let change = 0;
        let due = 0;

        if (method === 'Credit' && paid === 0) {
            due = grandTotal;
        } else if (paid >= grandTotal) {
            change = paid - grandTotal;
        } else {
            due = grandTotal - paid;
        }

        document.getElementById('posChangeDisplay').textContent = change.toFixed(2);

        const statusEl = document.getElementById('posPaymentStatusText');
        if (statusEl) {
            if (cart.length === 0) statusEl.textContent = 'Net Total';
            else if (due > 0) statusEl.textContent = `Balance Due: ${due.toFixed(2)}`;
            else statusEl.textContent = `Paid (${method})`;
        }
    }

    function onPaymentMethodChange() {
        recalculate(true);
    }

    function setExactCash() {
        const grandTotal = parseFloat(document.getElementById('posGrandTotalDisplay')?.textContent) || 0;
        const paidInput = document.getElementById('posPaidAmount');
        if (paidInput) {
            paidInput.value = grandTotal.toFixed(2);
            updateChangeAndDue();
        }
    }

    async function completeSale() {
        if (cart.length === 0) {
            showMessage('Please add at least one product to the basket before completing the sale.', 'warning');
            return;
        }

        const customerSelect = document.getElementById('posCustomerSelect');
        const customerId = parseInt(customerSelect?.value) || null;
        const selectedOpt = customerSelect?.options[customerSelect.selectedIndex];
        const isWalkIn = selectedOpt?.getAttribute('data-is-walkin') === 'true';

        const grandTotal = parseFloat(document.getElementById('posGrandTotalDisplay')?.textContent) || 0;
        const paid = Math.max(0, parseFloat(document.getElementById('posPaidAmount')?.value) || 0);
        const method = document.getElementById('posPaymentMethod')?.value || 'Cash';
        const due = (method === 'Credit' && paid === 0) ? grandTotal : Math.max(0, grandTotal - paid);

        if (due > 0 && isWalkIn) {
            showMessage('Walk-in Customer cannot purchase on credit. Please select a registered customer (F3) or enter full payment.', 'danger');
            return;
        }

        const payload = {
            customerId: customerId,
            priceTier: document.getElementById('posPriceTier')?.value || 'Retail',
            invoiceDiscountAmount: parseFloat(document.getElementById('posInvoiceDiscount')?.value) || 0,
            paidAmount: parseFloat(document.getElementById('posPaidAmount')?.value) || 0,
            paymentMethod: document.getElementById('posPaymentMethod')?.value || 'Cash',
            heldSaleId: currentHeldSaleId,
            items: cart.map(c => ({
                productId: c.productId,
                productName: c.name,
                sku: c.sku,
                quantity: c.quantity,
                unitPrice: c.unitPrice,
                discountAmount: c.discountAmount,
                taxRate: c.taxRate
            }))
        };

        const btn = document.getElementById('btnCompleteSale');
        if (btn) btn.disabled = true;

        try {
            const resp = await fetch('/POS/CompleteSale', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getAntiForgeryToken(),
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: JSON.stringify(payload)
            });

            const result = await resp.json();
            if (!resp.ok || !result.success) {
                showMessage(result.message || 'Could not complete sale.', 'danger');
                return;
            }

            // Update stock numbers on tiles in the UI immediately
            cart.forEach(item => {
                const tile = document.querySelector(`.pos-dense-product-tile[data-product-id="${item.productId}"]`);
                if (tile) {
                    const oldStock = parseFloat(tile.getAttribute('data-stock')) || 0;
                    const newStock = Math.max(0, oldStock - item.quantity);
                    tile.setAttribute('data-stock', newStock);
                    const stockEl = tile.querySelector('.pos-tile-stock');
                    if (stockEl) stockEl.textContent = newStock.toFixed(0);
                }
            });

            lastReceiptUrl = result.receiptUrl;
            const printBtn = document.getElementById('btnPrintLastReceipt');
            if (printBtn) printBtn.disabled = false;

            document.getElementById('completedInvoiceNum').textContent = result.invoiceNumber;
            document.getElementById('completedTotalAmt').textContent = result.totalAmount.toFixed(2);
            document.getElementById('completedPaidAmt').textContent = result.paidAmount.toFixed(2);
            document.getElementById('completedChangeAmt').textContent = result.changeAmount.toFixed(2);
            document.getElementById('completedDueAmt').textContent = result.dueAmount.toFixed(2);
            document.getElementById('btnThermalReceiptLink').href = `${result.receiptUrl}?format=Thermal`;
            document.getElementById('btnA4InvoiceLink').href = `${result.receiptUrl}?format=A4`;

            clearCart();

            const modalEl = document.getElementById('saleCompletedModal');
            if (modalEl && window.bootstrap) {
                const modal = new bootstrap.Modal(modalEl);
                modal.show();
            } else {
                showMessage(`Sale ${result.invoiceNumber} completed!`, 'success');
            }
        } catch {
            showMessage('Network or server error while completing sale.', 'danger');
        } finally {
            if (btn) btn.disabled = false;
        }
    }

    async function holdCurrentSale() {
        if (cart.length === 0) {
            showMessage('Cannot hold an empty basket.', 'warning');
            return;
        }

        const custSel = document.getElementById('posCustomerSelect');
        const custName = custSel?.options[custSel.selectedIndex]?.text || 'Walk-in Customer';
        const grandTotal = parseFloat(document.getElementById('posGrandTotalDisplay')?.textContent) || 0;

        const payload = {
            customerId: parseInt(custSel?.value) || null,
            customerName: custName,
            cartJson: JSON.stringify(cart),
            totalAmount: grandTotal
        };

        try {
            const resp = await fetch('/POS/HoldSale', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getAntiForgeryToken()
                },
                body: JSON.stringify(payload)
            });
            const data = await resp.json();
            if (data.success) {
                const badge = document.getElementById('heldCountBadge');
                if (badge) badge.textContent = (parseInt(badge.textContent) || 0) + 1;
                clearCart();
                showMessage(`Sale held under reference ${data.holdReference}. Press F5 to view or recall.`, 'info');
                setTimeout(() => window.location.reload(), 800);
            }
        } catch {
            showMessage('Could not hold sale.', 'danger');
        }
    }

    function resumeHeldSale(id, btnEl) {
        const row = btnEl.closest('tr');
        if (!row) return;
        const json = row.getAttribute('data-cart-json');
        const custId = row.getAttribute('data-customer-id');
        try {
            cart = JSON.parse(json) || [];
            currentHeldSaleId = id;
            if (custId) {
                const sel = document.getElementById('posCustomerSelect');
                if (sel) sel.value = custId;
            }
            renderCart();
            const modalEl = document.getElementById('heldSalesModal');
            if (modalEl && window.bootstrap) {
                bootstrap.Modal.getInstance(modalEl)?.hide();
            }
            showMessage('Held sale resumed into active basket.', 'info');
        } catch {
            showMessage('Failed to parse held sale items.', 'danger');
        }
    }

    async function deleteHeldSale(id, btnEl) {
        try {
            await fetch(`/POS/DeleteHeldSale?id=${id}`, {
                method: 'POST',
                headers: { 'RequestVerificationToken': getAntiForgeryToken() }
            });
            btnEl.closest('tr')?.remove();
            const badge = document.getElementById('heldCountBadge');
            if (badge) badge.textContent = Math.max(0, (parseInt(badge.textContent) || 1) - 1);
        } catch {
            showMessage('Failed to delete held sale.', 'danger');
        }
    }

    function printLastReceipt() {
        if (lastReceiptUrl) {
            window.open(lastReceiptUrl, '_blank');
        }
    }

    function filterProductCards() {
        const term = (document.getElementById('posSearchInput')?.value || '').trim().toLowerCase();
        const tiles = document.querySelectorAll('#posProductGrid .pos-dense-product-tile');
        tiles.forEach(tile => {
            const catId = parseInt(tile.getAttribute('data-category-id')) || 0;
            const name = (tile.getAttribute('data-name') || '').toLowerCase();
            const sku = (tile.getAttribute('data-sku') || '').toLowerCase();
            const barcode = (tile.getAttribute('data-barcode') || '').toLowerCase();

            const catMatch = activeCategoryId === 0 || catId === activeCategoryId;
            const termMatch = !term || name.includes(term) || sku.includes(term) || barcode.includes(term);

            tile.style.display = (catMatch && termMatch) ? '' : 'none';
        });
    }

    function init() {
        const barcodeInput = document.getElementById('posBarcodeInput');
        if (barcodeInput) {
            barcodeInput.addEventListener('input', function () {
                const val = this.value;
                showSuggestions(val);

                // If exact SKU/barcode typed, preview its rate
                const clean = val.trim().toLowerCase();
                if (clean) {
                    const exact = getAllCatalogProductsFromDom().find(p =>
                        p.sku.toLowerCase() === clean || p.barcode.toLowerCase() === clean
                    );
                    if (exact) {
                        stagedProduct = exact;
                        const pr = getPriceForTier(exact.salePrice, exact.wholesalePrice);
                        document.getElementById('posEntryRate').value = pr.toFixed(2);
                        updateEntryPreview();
                    } else {
                        stagedProduct = null;
                    }
                }
            });

            barcodeInput.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    commitEntryRow();
                } else if (e.key === 'Escape') {
                    hideSuggestions();
                }
            });
        }

        const entryQty = document.getElementById('posEntryQty');
        if (entryQty) {
            entryQty.addEventListener('input', updateEntryPreview);
            entryQty.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    commitEntryRow();
                }
            });
        }

        const entryRate = document.getElementById('posEntryRate');
        if (entryRate) {
            entryRate.addEventListener('input', updateEntryPreview);
            entryRate.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    commitEntryRow();
                }
            });
        }

        document.addEventListener('click', function (e) {
            if (!e.target.closest('.pos-field-itemcode')) {
                hideSuggestions();
            }
        });

        const searchInput = document.getElementById('posSearchInput');
        if (searchInput) {
            searchInput.addEventListener('input', function () {
                clearTimeout(searchDebounceTimer);
                searchDebounceTimer = setTimeout(filterProductCards, 100);
            });
        }

        document.getElementById('btnResetSearch')?.addEventListener('click', function () {
            if (searchInput) searchInput.value = '';
            filterProductCards();
            barcodeInput?.focus();
        });

        document.querySelectorAll('#posCategoryBar .pos-category-btn').forEach(btn => {
            btn.addEventListener('click', function () {
                document.querySelectorAll('#posCategoryBar .pos-category-btn').forEach(b => {
                    b.classList.remove('btn-primary', 'active');
                    b.classList.add('btn-outline-secondary');
                });
                this.classList.remove('btn-outline-secondary');
                this.classList.add('btn-primary', 'active');
                activeCategoryId = parseInt(this.getAttribute('data-category-id')) || 0;
                filterProductCards();
            });
        });

        // Keyboard Shortcuts (F2 - F9, ESC)
        window.addEventListener('keydown', function (e) {
            switch (e.key) {
                case 'F2':
                    e.preventDefault();
                    document.getElementById('posBarcodeInput')?.focus();
                    document.getElementById('posBarcodeInput')?.select();
                    break;
                case 'F3':
                    e.preventDefault();
                    document.getElementById('posCustomerSelect')?.focus();
                    break;
                case 'F4':
                    e.preventDefault();
                    holdCurrentSale();
                    break;
                case 'F5':
                    e.preventDefault();
                    const heldModalEl = document.getElementById('heldSalesModal');
                    if (heldModalEl && window.bootstrap) {
                        new bootstrap.Modal(heldModalEl).show();
                    }
                    break;
                case 'F6':
                    e.preventDefault();
                    document.getElementById('posPaidAmount')?.focus();
                    document.getElementById('posPaidAmount')?.select();
                    break;
                case 'F7':
                    e.preventDefault();
                    document.getElementById('posInvoiceDiscount')?.focus();
                    document.getElementById('posInvoiceDiscount')?.select();
                    break;
                case 'F8':
                    e.preventDefault();
                    clearCart();
                    break;
                case 'F9':
                    e.preventDefault();
                    completeSale();
                    break;
            }
        });
    }

    document.addEventListener('DOMContentLoaded', init);

    return {
        addProductFromCard,
        commitEntryRow,
        selectSuggestion,
        selectBasketRow,
        changeQty,
        setQty,
        setUnitPrice,
        setItemDiscount,
        removeItem,
        clearCart,
        onPriceTierChange,
        recalculate,
        updateChangeAndDue,
        onPaymentMethodChange,
        setExactCash,
        completeSale,
        holdCurrentSale,
        resumeHeldSale,
        deleteHeldSale,
        printLastReceipt
    };
})();
