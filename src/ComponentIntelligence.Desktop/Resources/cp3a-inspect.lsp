(vl-load-com)

(defun ci-cp3a-escape (s / i ch r)
  (if (null s) (setq s ""))
  (setq i 1 r "")
  (while (<= i (strlen s))
    (setq ch (substr s i 1))
    (setq r (strcat r
      (cond
        ((= ch "\\") "\\\\")
        ((= ch (chr 9)) "\\t")
        ((= ch (chr 13)) "\\r")
        ((= ch (chr 10)) "\\n")
        (T ch))))
    (setq i (1+ i)))
  r)

(defun ci-cp3a-write (fh parts)
  (write-line (apply 'strcat (cons (car parts) (mapcar '(lambda (p) (strcat (chr 9) p)) (cdr parts)))) fh))

(defun CI_CP3A_INSPECT (outputPath / doc blocks ms fh obj name attrs att minp maxp allmin allmax b)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
  (setq blocks (vla-get-Blocks doc))
  (setq ms (vla-get-ModelSpace doc))
  (setq fh (open outputPath "w"))
  (vlax-for b blocks
    (setq name (vla-get-Name b))
    (if (and (/= name "*Model_Space") (/= name "*Paper_Space"))
      (ci-cp3a-write fh (list "BLOCK" (ci-cp3a-escape name)))))
  (vlax-for obj ms
    (if (vlax-method-applicable-p obj 'GetBoundingBox)
      (progn
        (vla-GetBoundingBox obj 'minp 'maxp)
        (setq minp (vlax-safearray->list minp))
        (setq maxp (vlax-safearray->list maxp))
        (if (null allmin)
          (progn (setq allmin minp) (setq allmax maxp))
          (progn
            (setq allmin (mapcar 'min allmin minp))
            (setq allmax (mapcar 'max allmax maxp))))))
    (if (member (vla-get-ObjectName obj) '("AcDbText" "AcDbMText"))
      (ci-cp3a-write fh (list "TEXT" (ci-cp3a-escape (vla-get-TextString obj)))))
    (if (and (= (vla-get-ObjectName obj) "AcDbBlockReference") (= (vla-get-HasAttributes obj) :vlax-true))
      (progn
        (setq attrs (vlax-invoke obj 'GetAttributes))
        (foreach att attrs
          (ci-cp3a-write fh (list "ATTR" (ci-cp3a-escape (vla-get-TagString att)) (ci-cp3a-escape (vla-get-TextString att))))))))
  (if allmin
    (ci-cp3a-write fh (list "BBOX"
      (rtos (nth 0 allmin) 2 12) (rtos (nth 1 allmin) 2 12)
      (rtos (nth 0 allmax) 2 12) (rtos (nth 1 allmax) 2 12))))
  (close fh)
  (princ))

(princ)
