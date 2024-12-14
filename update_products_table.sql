-- Добавляем новые колонки в таблицу Products
ALTER TABLE Products
ADD COLUMN IF NOT EXISTS stock_quantity INTEGER DEFAULT 0,
ADD COLUMN IF NOT EXISTS is_available BOOLEAN DEFAULT true;

-- Обновляем существующие записи
UPDATE Products SET stock_quantity = 100 WHERE stock_quantity = 0;
