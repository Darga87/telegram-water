-- Добавляем новые столбцы в таблицу orders
ALTER TABLE orders
ADD COLUMN IF NOT EXISTS delivery_date DATE,
ADD COLUMN IF NOT EXISTS delivery_time TIME;

-- Обновляем существующие записи (опционально)
UPDATE orders
SET delivery_date = CURRENT_DATE,
    delivery_time = '12:00:00'
WHERE delivery_date IS NULL
   OR delivery_time IS NULL;
